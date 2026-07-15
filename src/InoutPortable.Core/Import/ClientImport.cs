using System.Data;
using InoutPortable.Core.Database;
using InoutPortable.Core.Models;
using InoutPortable.Core.Validation;
using Microsoft.Data.SqlClient;

namespace InoutPortable.Core.Import;

/// <summary>Where a sheet column lands: the organization table (fiscal identity) or the client table.</summary>
public sealed record ClientTarget(bool IsOrg, ColumnMetadata Column);

/// <summary>
/// Maps sheet columns (the CLIENTES view's column names, e.g. NOMCLI/NIFCLI/DIRCLI) to the real
/// base-table columns. In this a3ERP the CLIENTES view is <c>__CLIENTES C JOIN __ORGANIZACION O ON
/// C.IDORG=O.IDORG</c>, and the fiscal fields are exposed under renamed aliases (NOMCLI=O.NOMBRE,
/// NIFCLI=O.NIF, ...). See memory a3erp-cliente-alta-model.
/// </summary>
public static class ClientColumnMapper
{
    // View aliases that RENAME an __ORGANIZACION column (only the renamed ones; same-name columns
    // like RAZON/CODPROVI/E_MAIL are resolved directly against __ORGANIZACION below).
    private static readonly Dictionary<string, string> OrgAlias = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NOMCLI"] = "NOMBRE",
        ["TELCLI"] = "TELEFONO",
        ["TELCLI2"] = "TELEFONO2",
        ["POBCLI"] = "POBLACION",
        ["NIFCLI"] = "NIF",
        ["FAXCLI"] = "FAX",
        ["DTOCLI"] = "DTO",
        ["DIRCLI"] = "DIR",
        ["DIRCLI1"] = "DIR1",
        ["DIRCLI2"] = "DIR2",
        ["CLIALIAS"] = "ALIAS",
        // friendly extras a user might type
        ["NOMBRE"] = "NOMBRE",
        ["NIF"] = "NIF",
        ["EMAIL"] = "E_MAIL",
        ["CORREO"] = "E_MAIL",
        ["POBLACION"] = "POBLACION",
        ["TELEFONO"] = "TELEFONO",
        ["DIRECCION"] = "DIR",
    };

    /// <summary>Resolves a sheet column to its destination, or null if it matches no client/org column.</summary>
    public static ClientTarget? Resolve(string sheetColumn, TableMetadata clientTable, TableMetadata orgTable)
    {
        var name = sheetColumn.Trim();

        // 1) A __CLIENTES column always wins for its own fields (CODCLI, CUENTA, CODMON, ...).
        var cliCol = clientTable.GetColumn(name);
        if (cliCol is not null)
            return new ClientTarget(false, cliCol);

        // 2) A renamed/friendly organization alias.
        if (OrgAlias.TryGetValue(name, out var orgName))
        {
            var oc = orgTable.GetColumn(orgName);
            if (oc is not null) return new ClientTarget(true, oc);
        }

        // 3) A same-name organization column (RAZON, CODPROVI, CODPAIS, NOMFISCAL, ZONA, ...).
        var orgCol = orgTable.GetColumn(name);
        if (orgCol is not null)
            return new ClientTarget(true, orgCol);

        return null;
    }
}

/// <summary>
/// Builds an import plan for the CLIENTES view by splitting each row into an organization part and a
/// client part. Insert vs update is decided by whether the CODCLI already exists in __CLIENTES.
/// </summary>
public sealed class ClientImportPlanner
{
    private readonly ConnectionSettings _settings;
    private readonly TypeConverter _converter = new();

    public ClientImportPlanner(ConnectionSettings settings) => _settings = settings;

    public async Task<TableImportPlan> BuildPlanAsync(
        SheetData sheet, TableMetadata viewTable, TableMetadata clientTable, TableMetadata orgTable,
        CancellationToken ct = default)
    {
        var targets = new Dictionary<string, ClientTarget>(StringComparer.OrdinalIgnoreCase);
        var mapped = new List<string>();
        var plan = new TableImportPlan
        {
            Sheet = sheet.TableName,
            Table = viewTable,
            KeyColumns = new[] { "CODCLI" },
            MappedColumns = mapped,
            IsClientImport = true,
            ClientTable = clientTable,
            OrgTable = orgTable,
            ClientTargets = targets,
        };

        plan.Issues.Add(new ValidationIssue
        {
            Sheet = sheet.TableName,
            Message = "Alta/actualización de clientes: se escribirá en __ORGANIZACION + __CLIENTES (IDORG se genera automáticamente).",
            Severity = ValidationSeverity.Warning,
        });

        // Column mapping
        foreach (var col in sheet.Columns)
        {
            var t = ClientColumnMapper.Resolve(col, clientTable, orgTable);
            if (t is null)
            {
                plan.Issues.Add(ValidationIssue.Cell(sheet.TableName, sheet.HeaderRowNumber, col,
                    $"La columna '{col}' no corresponde a ningún campo de cliente y se ignorará.", ValidationSeverity.Warning));
                continue;
            }
            if (!targets.ContainsKey(col))
            {
                targets[col] = t;
                mapped.Add(col);
            }
        }

        // CODCLI is mandatory (it's the key).
        var codcliCol = sheet.Columns.FirstOrDefault(c =>
            targets.TryGetValue(c, out var t) && !t.IsOrg && t.Column.Name.Equals("CODCLI", StringComparison.OrdinalIgnoreCase));
        if (codcliCol is null)
        {
            plan.Issues.Add(ValidationIssue.Structural(sheet.TableName,
                "Falta la columna clave 'CODCLI' (código de cliente); es obligatoria para dar de alta o actualizar."));
            plan.IsBlocked = true;
            return plan;
        }

        // Name column (maps to __ORGANIZACION.NOMBRE) — required for new clients.
        bool hasName = targets.Values.Any(t => t.IsOrg && t.Column.Name.Equals("NOMBRE", StringComparison.OrdinalIgnoreCase));
        bool hasCuenta = targets.Values.Any(t => !t.IsOrg && t.Column.Name.Equals("CUENTA", StringComparison.OrdinalIgnoreCase));
        if (!hasCuenta)
            plan.Issues.Add(new ValidationIssue
            {
                Sheet = sheet.TableName,
                Message = "No se indica CUENTA (cuenta contable). Los clientes se crearán sin cuenta contable asignada; a3ERP la suele requerir para facturar.",
                Severity = ValidationSeverity.Warning,
            });

        // Existing CODCLIs -> classify insert/update.
        var codes = sheet.Rows
            .Select(r => r.Get(codcliCol).AsDisplayString().Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var existing = await GetExistingCodclisAsync(codes, ct);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in sheet.Rows)
        {
            ct.ThrowIfCancellationRequested();
            string codcli = row.Get(codcliCol).AsDisplayString().Trim();
            bool valid = true;

            if (codcli.Length == 0)
            {
                plan.Issues.Add(ValidationIssue.Cell(sheet.TableName, row.ExcelRowNumber, "CODCLI",
                    "El código de cliente (CODCLI) no puede estar vacío."));
                valid = false;
            }
            else if (!seen.Add(codcli))
            {
                plan.Issues.Add(ValidationIssue.Cell(sheet.TableName, row.ExcelRowNumber, "CODCLI",
                    $"Código de cliente duplicado en el Excel ('{codcli}'); solo se importa una vez."));
                valid = false;
            }

            bool isUpdate = valid && existing.Contains(codcli);

            // Convert mapped values.
            var converted = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var col in mapped)
            {
                var target = targets[col];
                var res = _converter.Convert(target.Column, row.Get(col));
                if (!res.Ok)
                {
                    plan.Issues.Add(ValidationIssue.Cell(sheet.TableName, row.ExcelRowNumber, col, res.Error!));
                    valid = false;
                    continue;
                }
                converted[col] = res.Value;
            }

            // New clients require a name.
            if (valid && !isUpdate)
            {
                bool nameEmpty = !hasName || mapped
                    .Where(c => targets[c].IsOrg && targets[c].Column.Name.Equals("NOMBRE", StringComparison.OrdinalIgnoreCase))
                    .All(c => converted.TryGetValue(c, out var v) && (v is null || v is DBNull || (v as string)?.Length == 0));
                if (nameEmpty)
                {
                    plan.Issues.Add(ValidationIssue.Cell(sheet.TableName, row.ExcelRowNumber, "NOMCLI",
                        $"El cliente nuevo '{codcli}' no tiene nombre (NOMCLI); es obligatorio para darlo de alta."));
                    valid = false;
                }
            }

            if (!valid)
            {
                plan.Rows.Add(new RowPlan { Row = row, Operation = RowOperation.Error, KeyDisplay = codcli });
                continue;
            }

            plan.Rows.Add(new RowPlan
            {
                Row = row,
                Operation = isUpdate ? RowOperation.Update : RowOperation.Insert,
                KeyDisplay = codcli,
                ConvertedValues = converted,
            });
        }

        return plan;
    }

    private async Task<HashSet<string>> GetExistingCodclisAsync(IReadOnlyList<string> codes, CancellationToken ct)
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
            cmd.CommandText = $"SELECT CODCLI FROM [dbo].[__CLIENTES] WHERE CODCLI IN ({string.Join(",", names)})";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                found.Add(r.GetString(0).Trim());
        }
        return found;
    }
}

/// <summary>
/// Writes a client import plan to the two base tables inside the caller's transaction:
/// resolves/creates the organization (dedup by NIF, IDORG allocated from IDENTIFICADORES) and then
/// inserts/updates the client row. Mirrors what a3ERP's own client writer does, minus the accounting
/// account creation. Constraints are suspended for the write (see UpsertExecutor / the original inout).
/// </summary>
internal static class ClientWriter
{
    public static async Task<TableImportResult> ExecuteAsync(
        SqlConnection conn, SqlTransaction tx, TableImportPlan plan, CancellationToken ct)
    {
        var org = plan.OrgTable!;
        var cli = plan.ClientTable!;
        var targets = plan.ClientTargets!;
        var result = new TableImportResult
        {
            Sheet = plan.Sheet,
            Table = plan.Table.FullName,
            SkippedWithErrors = plan.ErrorCount,
        };

        await UpsertExecutor.ExecuteRawAsync(conn, tx, "ALTER TABLE [dbo].[__ORGANIZACION] NOCHECK CONSTRAINT ALL", ct);
        await UpsertExecutor.ExecuteRawAsync(conn, tx, "ALTER TABLE [dbo].[__CLIENTES] NOCHECK CONSTRAINT ALL", ct);
        try
        {
            foreach (var row in plan.Rows)
            {
                ct.ThrowIfCancellationRequested();
                if (row.Operation == RowOperation.Error || row.ConvertedValues is null) continue;

                // Split the converted values into organization and client parts.
                var orgVals = new List<(ColumnMetadata Col, object? Val)>();
                var cliVals = new List<(ColumnMetadata Col, object? Val)>();
                string codcli = row.KeyDisplay ?? "";
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
                    else if (!target.Column.Name.Equals("CODCLI", StringComparison.OrdinalIgnoreCase))
                    {
                        cliVals.Add((target.Column, v));
                    }
                }

                if (row.Operation == RowOperation.Update)
                {
                    int idorg = await ScalarIntAsync(conn, tx,
                        "SELECT IDORG FROM [dbo].[__CLIENTES] WHERE CODCLI = @k",
                        ("@k", codcli), ct);
                    if (orgVals.Count > 0)
                        await UpdateByKeyAsync(conn, tx, "__ORGANIZACION", orgVals, "IDORG", SqlDbType.Int, idorg, ct);
                    if (cliVals.Count > 0)
                        await UpdateByKeyAsync(conn, tx, "__CLIENTES", cliVals, "CODCLI", SqlDbType.VarChar, codcli, ct);
                    result.Updated++;
                }
                else // Insert
                {
                    int idorg;
                    int? existingOrg = string.IsNullOrWhiteSpace(nif) ? null : await FindOrgByNifAsync(conn, tx, nif!, ct);
                    if (existingOrg is int reuse)
                    {
                        // Same fiscal entity (e.g. already a provider): reuse its organization and refresh its fields.
                        idorg = reuse;
                        if (orgVals.Count > 0)
                            await UpdateByKeyAsync(conn, tx, "__ORGANIZACION", orgVals, "IDORG", SqlDbType.Int, idorg, ct);
                    }
                    else
                    {
                        idorg = await AllocateIdOrgAsync(conn, tx, ct);
                        await InsertOrgAsync(conn, tx, org, idorg, orgVals, ct);
                    }
                    await InsertClientAsync(conn, tx, cli, codcli, idorg, cliVals, ct);
                    result.Inserted++;
                }
            }
        }
        finally
        {
            await UpsertExecutor.ExecuteRawAsync(conn, tx, "ALTER TABLE [dbo].[__CLIENTES] CHECK CONSTRAINT ALL", ct);
            await UpsertExecutor.ExecuteRawAsync(conn, tx, "ALTER TABLE [dbo].[__ORGANIZACION] CHECK CONSTRAINT ALL", ct);
        }

        result.Success = true;
        return result;
    }

    private static async Task<int> AllocateIdOrgAsync(SqlConnection conn, SqlTransaction tx, CancellationToken ct)
    {
        // Atomic bump of a3ERP's surrogate-id counter, returning the new value.
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
        // Default the fiscal name to the commercial name when not supplied, so the org isn't left half-empty.
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

    private static async Task InsertClientAsync(SqlConnection conn, SqlTransaction tx, TableMetadata cli,
        string codcli, int idorg, List<(ColumnMetadata Col, object? Val)> cliVals, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        var cols = new List<string> { "[CODCLI]", "[IDORG]" };
        var toks = new List<string> { "@CODCLI", "@IDORG" };
        cmd.Parameters.Add(new SqlParameter("@CODCLI", SqlDbType.VarChar, 8) { Value = codcli });
        cmd.Parameters.Add(new SqlParameter("@IDORG", SqlDbType.Int) { Value = idorg });
        int i = 0;
        foreach (var (col, val) in cliVals)
        {
            var p = "@c" + i++;
            cols.Add(UpsertExecutor.Bracket(col.Name));
            toks.Add(p);
            var param = UpsertExecutor.CreateParameter(p, col);
            param.Value = val ?? DBNull.Value;
            cmd.Parameters.Add(param);
        }
        cmd.CommandText = $"INSERT INTO [dbo].[__CLIENTES] ({string.Join(", ", cols)}) VALUES ({string.Join(", ", toks)})";
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
        cmd.CommandText = $"UPDATE [dbo].[{table}] SET {string.Join(", ", sets)} WHERE [{keyCol}] = @key";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<int> ScalarIntAsync(SqlConnection conn, SqlTransaction tx, string sql,
        (string Name, object Val) param, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter(param.Name, SqlDbType.VarChar, 8) { Value = param.Val });
        var obj = await cmd.ExecuteScalarAsync(ct);
        return obj is null || obj is DBNull ? 0 : Convert.ToInt32(obj);
    }
}
