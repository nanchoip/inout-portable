using System.Data;
using InoutPortable.Core.Database;
using InoutPortable.Core.Models;
using InoutPortable.Core.Validation;
using Microsoft.Data.SqlClient;

namespace InoutPortable.Core.Import;

/// <summary>Client or provider — the two "third parties" that share __ORGANIZACION.</summary>
public enum ThirdPartyKind { Client, Provider }

/// <summary>Static description of a third-party target (which view/base table/code column it uses).</summary>
public sealed record ThirdPartyKindInfo(
    ThirdPartyKind Kind, string ViewName, string BaseTable, string CodeColumn, string Label)
{
    public static readonly ThirdPartyKindInfo Client =
        new(ThirdPartyKind.Client, "CLIENTES", "__CLIENTES", "CODCLI", "clientes");
    public static readonly ThirdPartyKindInfo Provider =
        new(ThirdPartyKind.Provider, "PROVEED", "__PROVEED", "CODPRO", "proveedores");

    /// <summary>Resolves a resolved view name to its third-party info, or null if it isn't one.</summary>
    public static ThirdPartyKindInfo? ForView(string viewName) =>
        viewName.Equals("CLIENTES", StringComparison.OrdinalIgnoreCase) ? Client
        : viewName.Equals("PROVEED", StringComparison.OrdinalIgnoreCase) ? Provider
        : null;
}

/// <summary>Where a sheet column lands: the organization table (fiscal identity) or the client/provider table.</summary>
public sealed record ClientTarget(bool IsOrg, ColumnMetadata Column);

/// <summary>
/// Maps sheet columns (the CLIENTES/PROVEED view column names, e.g. NOMCLI/NIFCLI or NOMPRO/NIFPRO) to
/// the real base-table columns. Both views are <c>__X JOIN __ORGANIZACION O ON X.IDORG=O.IDORG</c>, and
/// the fiscal fields are exposed under renamed aliases (NOMCLI/NOMPRO = O.NOMBRE, ...). See memory
/// a3erp-cliente-alta-model.
/// </summary>
public static class ClientColumnMapper
{
    // View aliases that RENAME an __ORGANIZACION column (client + provider variants). Same-name columns
    // (RAZON/CODPROVI/E_MAIL/...) are resolved directly against __ORGANIZACION below.
    private static readonly Dictionary<string, string> OrgAlias = new(StringComparer.OrdinalIgnoreCase)
    {
        // clientes
        ["NOMCLI"] = "NOMBRE", ["TELCLI"] = "TELEFONO", ["TELCLI2"] = "TELEFONO2", ["POBCLI"] = "POBLACION",
        ["NIFCLI"] = "NIF", ["FAXCLI"] = "FAX", ["DTOCLI"] = "DTO", ["DIRCLI"] = "DIR",
        ["DIRCLI1"] = "DIR1", ["DIRCLI2"] = "DIR2", ["CLIALIAS"] = "ALIAS",
        // proveedores
        ["NOMPRO"] = "NOMBRE", ["TELPRO"] = "TELEFONO", ["TEL2PRO"] = "TELEFONO2", ["POBPRO"] = "POBLACION",
        ["NIFPRO"] = "NIF", ["FAXPRO"] = "FAX", ["DTOPRO"] = "DTO", ["DIRPRO"] = "DIR",
        ["DIRPRO1"] = "DIR1", ["DIRPRO2"] = "DIR2", ["PROALIAS"] = "ALIAS",
        // friendly extras
        ["NOMBRE"] = "NOMBRE", ["NIF"] = "NIF", ["EMAIL"] = "E_MAIL", ["CORREO"] = "E_MAIL",
        ["POBLACION"] = "POBLACION", ["TELEFONO"] = "TELEFONO", ["DIRECCION"] = "DIR",
    };

    /// <summary>Resolves a sheet column to its destination, or null if it matches no third-party/org column.</summary>
    public static ClientTarget? Resolve(string sheetColumn, TableMetadata baseTable, TableMetadata orgTable)
    {
        var name = sheetColumn.Trim();

        // 1) A base-table column wins for its own fields (CODCLI/CODPRO, CUENTA, CODMON, ...).
        var baseCol = baseTable.GetColumn(name);
        if (baseCol is not null)
            return new ClientTarget(false, baseCol);

        // 2) A renamed/friendly organization alias.
        if (OrgAlias.TryGetValue(name, out var orgName) && orgTable.GetColumn(orgName) is { } oc)
            return new ClientTarget(true, oc);

        // 3) A same-name organization column (RAZON, CODPROVI, CODPAIS, NOMFISCAL, ZONA, ...).
        if (orgTable.GetColumn(name) is { } orgCol)
            return new ClientTarget(true, orgCol);

        return null;
    }
}

/// <summary>
/// Builds an import plan for the CLIENTES/PROVEED view by splitting each row into an organization part
/// and a client/provider part. Insert vs update is decided by whether the code already exists.
/// </summary>
public sealed class ClientImportPlanner
{
    private readonly ConnectionSettings _settings;
    private readonly TypeConverter _converter = new();

    public ClientImportPlanner(ConnectionSettings settings) => _settings = settings;

    public async Task<TableImportPlan> BuildPlanAsync(
        SheetData sheet, TableMetadata viewTable, ThirdPartyKindInfo info,
        TableMetadata baseTable, TableMetadata orgTable, CancellationToken ct = default)
    {
        var targets = new Dictionary<string, ClientTarget>(StringComparer.OrdinalIgnoreCase);
        var mapped = new List<string>();
        var plan = new TableImportPlan
        {
            Sheet = sheet.TableName,
            Table = viewTable,
            KeyColumns = new[] { info.CodeColumn },
            MappedColumns = mapped,
            IsThirdPartyImport = true,
            ThirdPartyBaseTable = info.BaseTable,
            ThirdPartyCodeColumn = info.CodeColumn,
            OrgTable = orgTable,
            ClientTargets = targets,
        };

        plan.Issues.Add(new ValidationIssue
        {
            Sheet = sheet.TableName,
            Message = $"Alta/actualización de {info.Label}: se escribirá en __ORGANIZACION + {info.BaseTable} (IDORG se genera automáticamente).",
            Severity = ValidationSeverity.Warning,
        });

        foreach (var col in sheet.Columns)
        {
            var t = ClientColumnMapper.Resolve(col, baseTable, orgTable);
            if (t is null)
            {
                plan.Issues.Add(ValidationIssue.Cell(sheet.TableName, sheet.HeaderRowNumber, col,
                    $"La columna '{col}' no corresponde a ningún campo y se ignorará.", ValidationSeverity.Warning));
                continue;
            }
            if (targets.TryAdd(col, t))
                mapped.Add(col);
        }

        var codeCol = sheet.Columns.FirstOrDefault(c =>
            targets.TryGetValue(c, out var t) && !t.IsOrg
            && t.Column.Name.Equals(info.CodeColumn, StringComparison.OrdinalIgnoreCase));
        if (codeCol is null)
        {
            plan.Issues.Add(ValidationIssue.Structural(sheet.TableName,
                $"Falta la columna clave '{info.CodeColumn}' (código); es obligatoria para dar de alta o actualizar."));
            plan.IsBlocked = true;
            return plan;
        }

        bool hasName = targets.Values.Any(t => t.IsOrg && t.Column.Name.Equals("NOMBRE", StringComparison.OrdinalIgnoreCase));
        bool hasCuenta = targets.Values.Any(t => !t.IsOrg && t.Column.Name.Equals("CUENTA", StringComparison.OrdinalIgnoreCase));
        if (!hasCuenta)
            plan.Issues.Add(new ValidationIssue
            {
                Sheet = sheet.TableName,
                Message = "No se indica CUENTA (cuenta contable). Se crearán sin cuenta contable asignada; a3ERP la suele requerir para facturar.",
                Severity = ValidationSeverity.Warning,
            });

        var codes = sheet.Rows
            .Select(r => r.Get(codeCol).AsDisplayString().Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var existing = await GetExistingCodesAsync(info, codes, ct);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in sheet.Rows)
        {
            ct.ThrowIfCancellationRequested();
            string code = row.Get(codeCol).AsDisplayString().Trim();
            bool valid = true;

            if (code.Length == 0)
            {
                plan.Issues.Add(ValidationIssue.Cell(sheet.TableName, row.ExcelRowNumber, info.CodeColumn,
                    $"El código ({info.CodeColumn}) no puede estar vacío."));
                valid = false;
            }
            else if (!seen.Add(code))
            {
                plan.Issues.Add(ValidationIssue.Cell(sheet.TableName, row.ExcelRowNumber, info.CodeColumn,
                    $"Código duplicado en el Excel ('{code}'); solo se importa una vez."));
                valid = false;
            }

            bool isUpdate = valid && existing.Contains(code);

            var converted = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var col in mapped)
            {
                var res = _converter.Convert(targets[col].Column, row.Get(col));
                if (!res.Ok)
                {
                    plan.Issues.Add(ValidationIssue.Cell(sheet.TableName, row.ExcelRowNumber, col, res.Error!));
                    valid = false;
                    continue;
                }
                converted[col] = res.Value;
            }

            if (valid && !isUpdate)
            {
                bool nameEmpty = !hasName || mapped
                    .Where(c => targets[c].IsOrg && targets[c].Column.Name.Equals("NOMBRE", StringComparison.OrdinalIgnoreCase))
                    .All(c => converted.TryGetValue(c, out var v) && (v is null || v is DBNull || (v as string)?.Length == 0));
                if (nameEmpty)
                {
                    plan.Issues.Add(ValidationIssue.Cell(sheet.TableName, row.ExcelRowNumber, "NOMBRE",
                        $"El registro nuevo '{code}' no tiene nombre; es obligatorio para darlo de alta."));
                    valid = false;
                }
            }

            if (!valid)
            {
                plan.Rows.Add(new RowPlan { Row = row, Operation = RowOperation.Error, KeyDisplay = code });
                continue;
            }

            plan.Rows.Add(new RowPlan
            {
                Row = row,
                Operation = isUpdate ? RowOperation.Update : RowOperation.Insert,
                KeyDisplay = code,
                ConvertedValues = converted,
            });
        }

        return plan;
    }

    private async Task<HashSet<string>> GetExistingCodesAsync(ThirdPartyKindInfo info, IReadOnlyList<string> codes, CancellationToken ct)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (codes.Count == 0) return found;

        await using var conn = new SqlConnection(_settings.BuildConnectionString());
        await conn.OpenAsync(ct);

        const int batch = 500;
        for (int i = 0; i < codes.Count; i += batch)
        {
            var slice = codes.Skip(i).Take(batch).ToList();
            await using var cmd = conn.CreateCommand();
            var names = new List<string>(slice.Count);
            for (int j = 0; j < slice.Count; j++)
            {
                var p = "@c" + j;
                names.Add(p);
                cmd.Parameters.Add(new SqlParameter(p, SqlDbType.VarChar, 8) { Value = slice[j] });
            }
            cmd.CommandText = $"SELECT {info.CodeColumn} FROM [dbo].[{info.BaseTable}] WHERE {info.CodeColumn} IN ({string.Join(",", names)})";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                found.Add(r.GetString(0).Trim());
        }
        return found;
    }
}

/// <summary>
/// Writes a third-party (client/provider) import plan to the two base tables inside the caller's
/// transaction: resolves/creates the organization (dedup by NIF, IDORG allocated from IDENTIFICADORES)
/// and then inserts/updates the client/provider row. Constraints are suspended for the write.
/// Does NOT create the accounting account (CUENTAS) — CUENTA is taken from the sheet if present.
/// </summary>
internal static class ClientWriter
{
    public static async Task<TableImportResult> ExecuteAsync(
        SqlConnection conn, SqlTransaction tx, TableImportPlan plan, CancellationToken ct)
    {
        var org = plan.OrgTable!;
        var baseTable = plan.ThirdPartyBaseTable!;
        var codeColumn = plan.ThirdPartyCodeColumn!;
        var targets = plan.ClientTargets!;
        var result = new TableImportResult
        {
            Sheet = plan.Sheet,
            Table = plan.Table.FullName,
            SkippedWithErrors = plan.ErrorCount,
        };

        await UpsertExecutor.ExecuteRawAsync(conn, tx, "ALTER TABLE [dbo].[__ORGANIZACION] NOCHECK CONSTRAINT ALL", ct);
        await UpsertExecutor.ExecuteRawAsync(conn, tx, $"ALTER TABLE [dbo].[{baseTable}] NOCHECK CONSTRAINT ALL", ct);
        try
        {
            foreach (var row in plan.Rows)
            {
                ct.ThrowIfCancellationRequested();
                if (row.Operation == RowOperation.Error || row.ConvertedValues is null) continue;

                var orgVals = new List<(ColumnMetadata Col, object? Val)>();
                var baseVals = new List<(ColumnMetadata Col, object? Val)>();
                string code = row.KeyDisplay ?? "";
                string? nif = null;

                foreach (var (sheetCol, target) in targets)
                {
                    if (!row.ConvertedValues.TryGetValue(sheetCol, out var v)) continue;
                    if (target.IsOrg)
                    {
                        orgVals.Add((target.Column, v));
                        if (target.Column.Name.Equals("NIF", StringComparison.OrdinalIgnoreCase))
                            nif = v as string;
                    }
                    else if (!target.Column.Name.Equals(codeColumn, StringComparison.OrdinalIgnoreCase))
                    {
                        baseVals.Add((target.Column, v));
                    }
                }

                if (row.Operation == RowOperation.Update)
                {
                    int idorg = await ScalarIntAsync(conn, tx,
                        $"SELECT IDORG FROM [dbo].[{baseTable}] WHERE {codeColumn} = @k", code, ct);
                    if (orgVals.Count > 0)
                        await UpdateByKeyAsync(conn, tx, "__ORGANIZACION", orgVals, "IDORG", SqlDbType.Int, idorg, ct);
                    if (baseVals.Count > 0)
                        await UpdateByKeyAsync(conn, tx, baseTable, baseVals, codeColumn, SqlDbType.VarChar, code, ct);
                    result.Updated++;
                }
                else
                {
                    int? existingOrg = string.IsNullOrWhiteSpace(nif) ? null : await FindOrgByNifAsync(conn, tx, nif!, ct);
                    int idorg;
                    if (existingOrg is int reuse)
                    {
                        idorg = reuse;
                        if (orgVals.Count > 0)
                            await UpdateByKeyAsync(conn, tx, "__ORGANIZACION", orgVals, "IDORG", SqlDbType.Int, idorg, ct);
                    }
                    else
                    {
                        idorg = await AllocateIdOrgAsync(conn, tx, ct);
                        await InsertOrgAsync(conn, tx, org, idorg, orgVals, ct);
                    }
                    await InsertBaseAsync(conn, tx, baseTable, codeColumn, code, idorg, baseVals, ct);
                    result.Inserted++;
                }
            }
        }
        finally
        {
            await UpsertExecutor.ExecuteRawAsync(conn, tx, $"ALTER TABLE [dbo].[{baseTable}] CHECK CONSTRAINT ALL", ct);
            await UpsertExecutor.ExecuteRawAsync(conn, tx, "ALTER TABLE [dbo].[__ORGANIZACION] CHECK CONSTRAINT ALL", ct);
        }

        result.Success = true;
        return result;
    }

    private static async Task<int> AllocateIdOrgAsync(SqlConnection conn, SqlTransaction tx, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "UPDATE IDENTIFICADORES SET VALOR = VALOR + 1 OUTPUT CONVERT(int, INSERTED.VALOR) WHERE NOMIDE = 'IDORG'";
        var obj = await cmd.ExecuteScalarAsync(ct);
        if (obj is null || obj is DBNull)
            throw new InvalidOperationException("No se pudo generar el IDORG (falta la fila 'IDORG' en IDENTIFICADORES).");
        return Convert.ToInt32(obj);
    }

    private static async Task<int?> FindOrgByNifAsync(SqlConnection conn, SqlTransaction tx, string nif, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT TOP 1 IDORG FROM [dbo].[__ORGANIZACION] WHERE NIF = @nif ORDER BY IDORG";
        cmd.Parameters.Add(new SqlParameter("@nif", SqlDbType.VarChar, 20) { Value = nif });
        var obj = await cmd.ExecuteScalarAsync(ct);
        return obj is null || obj is DBNull ? null : Convert.ToInt32(obj);
    }

    private static async Task InsertOrgAsync(SqlConnection conn, SqlTransaction tx, TableMetadata org,
        int idorg, List<(ColumnMetadata Col, object? Val)> orgVals, CancellationToken ct)
    {
        bool hasFiscal = orgVals.Any(v => v.Col.Name.Equals("NOMFISCAL", StringComparison.OrdinalIgnoreCase));
        var nombre = orgVals.FirstOrDefault(v => v.Col.Name.Equals("NOMBRE", StringComparison.OrdinalIgnoreCase));
        if (!hasFiscal && nombre.Col is not null && org.GetColumn("NOMFISCAL") is { } fiscalCol)
            orgVals = orgVals.Append((fiscalCol, nombre.Val)).ToList();

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        var cols = new List<string> { "[IDORG]" };
        var toks = new List<string> { "@IDORG" };
        cmd.Parameters.Add(new SqlParameter("@IDORG", SqlDbType.Int) { Value = idorg });
        int i = 0;
        foreach (var (col, val) in orgVals)
        {
            var p = "@o" + i++;
            cols.Add(UpsertExecutor.Bracket(col.Name));
            toks.Add(p);
            var param = UpsertExecutor.CreateParameter(p, col);
            param.Value = val ?? DBNull.Value;
            cmd.Parameters.Add(param);
        }
        cmd.CommandText = $"INSERT INTO [dbo].[__ORGANIZACION] ({string.Join(", ", cols)}) VALUES ({string.Join(", ", toks)})";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task InsertBaseAsync(SqlConnection conn, SqlTransaction tx, string baseTable,
        string codeColumn, string code, int idorg, List<(ColumnMetadata Col, object? Val)> baseVals, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        var cols = new List<string> { UpsertExecutor.Bracket(codeColumn), "[IDORG]" };
        var toks = new List<string> { "@CODE", "@IDORG" };
        cmd.Parameters.Add(new SqlParameter("@CODE", SqlDbType.VarChar, 8) { Value = code });
        cmd.Parameters.Add(new SqlParameter("@IDORG", SqlDbType.Int) { Value = idorg });
        int i = 0;
        foreach (var (col, val) in baseVals)
        {
            var p = "@c" + i++;
            cols.Add(UpsertExecutor.Bracket(col.Name));
            toks.Add(p);
            var param = UpsertExecutor.CreateParameter(p, col);
            param.Value = val ?? DBNull.Value;
            cmd.Parameters.Add(param);
        }
        cmd.CommandText = $"INSERT INTO [dbo].[{baseTable}] ({string.Join(", ", cols)}) VALUES ({string.Join(", ", toks)})";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task UpdateByKeyAsync(SqlConnection conn, SqlTransaction tx, string table,
        List<(ColumnMetadata Col, object? Val)> vals, string keyCol, SqlDbType keyType, object keyVal, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        var sets = new List<string>();
        int i = 0;
        foreach (var (col, val) in vals)
        {
            var p = "@u" + i++;
            sets.Add($"{UpsertExecutor.Bracket(col.Name)} = {p}");
            var param = UpsertExecutor.CreateParameter(p, col);
            param.Value = val ?? DBNull.Value;
            cmd.Parameters.Add(param);
        }
        var kp = keyType == SqlDbType.Int
            ? new SqlParameter("@key", SqlDbType.Int) { Value = keyVal }
            : new SqlParameter("@key", SqlDbType.VarChar, 8) { Value = keyVal };
        cmd.Parameters.Add(kp);
        cmd.CommandText = $"UPDATE [dbo].[{table}] SET {string.Join(", ", sets)} WHERE {UpsertExecutor.Bracket(keyCol)} = @key";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<int> ScalarIntAsync(SqlConnection conn, SqlTransaction tx, string sql, string keyVal, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@k", SqlDbType.VarChar, 8) { Value = keyVal });
        var obj = await cmd.ExecuteScalarAsync(ct);
        return obj is null || obj is DBNull ? 0 : Convert.ToInt32(obj);
    }
}
