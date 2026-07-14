using System.Data;
using InoutPortable.Core.Database;
using InoutPortable.Core.Models;
using Microsoft.Data.SqlClient;

namespace InoutPortable.Core.Import;

/// <summary>
/// Executes an <see cref="ImportPreview"/> against SQL Server. All sheets run inside a single
/// transaction: if anything fails, the whole run is rolled back so no partial data is left behind.
/// </summary>
public sealed class UpsertExecutor
{
    private readonly ConnectionSettings _settings;

    public UpsertExecutor(ConnectionSettings settings) => _settings = settings;

    public async Task<ImportResult> ExecuteAsync(
        ImportPreview preview,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var result = new ImportResult { FileName = preview.FileName };

        var tables = preview.Tables
            .Where(t => t.IsImportable)
            .ToList();

        if (tables.Count == 0)
        {
            result.Success = false;
            result.Message = "No hay filas válidas para importar.";
            result.FinishedAt = DateTime.Now;
            return result;
        }

        await using var conn = new SqlConnection(_settings.BuildConnectionString());
        await conn.OpenAsync(ct);
        using var tx = conn.BeginTransaction();

        try
        {
            foreach (var table in tables)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"Importando '{table.Sheet}' ({table.InsertCount} insertar, {table.UpdateCount} actualizar)...");
                var tableResult = await ExecuteTableAsync(conn, tx, table, ct);
                result.Tables.Add(tableResult);
            }

            tx.Commit();
            result.Success = true;
            result.Message = $"Importación completada: {result.TotalInserted} insertadas, {result.TotalUpdated} actualizadas.";
        }
        catch (Exception ex)
        {
            SafeRollback(tx);
            result.Success = false;
            result.RolledBack = true;
            result.Message = $"La importación falló y se revirtió por completo (rollback). Detalle: {ex.Message}";

            // Ensure every table shows as not-committed.
            foreach (var t in result.Tables)
            {
                t.Success = false;
                t.Inserted = 0;
                t.Updated = 0;
            }
        }

        result.FinishedAt = DateTime.Now;
        return result;
    }

    private static async Task<TableImportResult> ExecuteTableAsync(
        SqlConnection conn, SqlTransaction tx, TableImportPlan plan, CancellationToken ct)
    {
        var table = plan.Table;
        var result = new TableImportResult
        {
            Sheet = plan.Sheet,
            Table = table.FullName,
            SkippedWithErrors = plan.ErrorCount,
        };

        var writableMapped = plan.MappedColumns
            .Select(c => table.GetColumn(c)!)
            .Where(c => c.IsWritable)
            .ToList();

        var identityCol = writableMapped.FirstOrDefault(c => c.IsIdentity);
        bool hasInserts = plan.Rows.Any(r => r.Operation == RowOperation.Insert);
        bool identityInsert = identityCol is not null && hasInserts;

        var keySet = new HashSet<string>(plan.KeyColumns, StringComparer.OrdinalIgnoreCase);
        var insertCols = writableMapped.Where(c => !c.IsIdentity || identityInsert).Select(c => c.Name).ToList();
        var setCols = writableMapped.Where(c => !c.IsIdentity && !keySet.Contains(c.Name)).Select(c => c.Name).ToList();

        if (identityInsert)
            await ExecuteRawAsync(conn, tx, $"SET IDENTITY_INSERT {table.FullName} ON", ct);

        // Suspend CHECK/FK constraints for the load, exactly as the original a3ERP inout does
        // (`ALTER TABLE ... NOCHECK CONSTRAINT ALL`). a3ERP guards columns such as account references
        // with cross-table CHECK constraints; exported/imported data can legitimately reference values
        // this instance doesn't hold, so the constraints are disabled for the write and re-enabled after.
        await ExecuteRawAsync(conn, tx, $"ALTER TABLE {table.FullName} NOCHECK CONSTRAINT ALL", ct);

        try
        {
            using var insertCmd = BuildInsertCommand(conn, tx, table, insertCols);
            using var updateCmd = setCols.Count > 0 ? BuildUpdateCommand(conn, tx, table, setCols, plan.KeyColumns) : null;

            foreach (var row in plan.Rows)
            {
                ct.ThrowIfCancellationRequested();
                var values = row.ConvertedValues;
                if (values is null) continue; // error rows

                if (row.Operation == RowOperation.Insert)
                {
                    if (identityInsert && IsNull(values, identityCol!.Name))
                        throw new InvalidOperationException(
                            $"La fila con clave '{row.KeyDisplay}' no tiene valor para la columna de identidad '{identityCol!.Name}', necesaria para insertarla.");

                    for (int i = 0; i < insertCols.Count; i++)
                        insertCmd.Parameters[i].Value = Val(values, insertCols[i]);
                    await insertCmd.ExecuteNonQueryAsync(ct);
                    result.Inserted++;
                }
                else if (row.Operation == RowOperation.Update)
                {
                    if (updateCmd is null)
                    {
                        // Nothing but key columns mapped: row already matches, nothing to change.
                        result.Updated++;
                        continue;
                    }

                    int p = 0;
                    foreach (var c in setCols)
                        updateCmd.Parameters[p++].Value = Val(values, c);
                    foreach (var k in plan.KeyColumns)
                        updateCmd.Parameters[p++].Value = Val(values, k);
                    await updateCmd.ExecuteNonQueryAsync(ct);
                    result.Updated++;
                }
            }
        }
        finally
        {
            if (identityInsert)
                await ExecuteRawAsync(conn, tx, $"SET IDENTITY_INSERT {table.FullName} OFF", ct);
        }

        // Re-enable the constraints without re-validating existing rows (same as the original inout).
        // On failure the surrounding transaction rolls back, which reverts the NOCHECK automatically.
        await ExecuteRawAsync(conn, tx, $"ALTER TABLE {table.FullName} CHECK CONSTRAINT ALL", ct);

        result.Success = true;
        return result;
    }

    private static SqlCommand BuildInsertCommand(SqlConnection conn, SqlTransaction tx, TableMetadata table, IReadOnlyList<string> cols)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        var colList = string.Join(", ", cols.Select(Bracket));
        var paramList = string.Join(", ", cols.Select((_, i) => "@p" + i));
        cmd.CommandText = $"INSERT INTO {table.FullName} ({colList}) VALUES ({paramList})";
        for (int i = 0; i < cols.Count; i++)
            cmd.Parameters.Add(CreateParameter("@p" + i, table.GetColumn(cols[i])!));
        cmd.Prepare();
        return cmd;
    }

    private static SqlCommand BuildUpdateCommand(SqlConnection conn, SqlTransaction tx, TableMetadata table,
        IReadOnlyList<string> setCols, IReadOnlyList<string> keyCols)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        var setList = string.Join(", ", setCols.Select((c, i) => $"{Bracket(c)} = @s{i}"));
        var whereList = string.Join(" AND ", keyCols.Select((c, i) => $"{Bracket(c)} = @k{i}"));
        cmd.CommandText = $"UPDATE {table.FullName} SET {setList} WHERE {whereList}";
        for (int i = 0; i < setCols.Count; i++)
            cmd.Parameters.Add(CreateParameter("@s" + i, table.GetColumn(setCols[i])!));
        for (int i = 0; i < keyCols.Count; i++)
            cmd.Parameters.Add(CreateParameter("@k" + i, table.GetColumn(keyCols[i])!));
        cmd.Prepare();
        return cmd;
    }

    /// <summary>Creates a parameter with an explicit SqlDbType (and size/precision) so Prepare() works
    /// and the value goes on the wire as the exact column type.</summary>
    private static SqlParameter CreateParameter(string name, ColumnMetadata col)
    {
        var p = new SqlParameter(name, MapSqlDbType(col.SqlType)) { Value = DBNull.Value, IsNullable = true };

        switch (p.SqlDbType)
        {
            case SqlDbType.VarChar or SqlDbType.NVarChar or SqlDbType.Char or SqlDbType.NChar
                or SqlDbType.Binary or SqlDbType.VarBinary:
                p.Size = col.MaxLength is > 0 ? col.MaxLength.Value : -1; // -1 => MAX
                break;
            case SqlDbType.Text or SqlDbType.NText or SqlDbType.Image or SqlDbType.Xml:
                // LOB types are variable-length; Prepare() rejects Size 0, so use -1 (MAX).
                p.Size = -1;
                break;
            case SqlDbType.Decimal:
                p.Precision = col.Precision ?? 18;
                p.Scale = (byte)(col.Scale ?? 0);
                break;
        }
        return p;
    }

    private static SqlDbType MapSqlDbType(string sqlType) => sqlType.ToLowerInvariant() switch
    {
        "varchar" => SqlDbType.VarChar,
        "nvarchar" or "sysname" => SqlDbType.NVarChar,
        "char" => SqlDbType.Char,
        "nchar" => SqlDbType.NChar,
        "text" => SqlDbType.Text,
        "ntext" => SqlDbType.NText,
        "xml" => SqlDbType.Xml,
        "tinyint" => SqlDbType.TinyInt,
        "smallint" => SqlDbType.SmallInt,
        "int" => SqlDbType.Int,
        "bigint" => SqlDbType.BigInt,
        "decimal" or "numeric" => SqlDbType.Decimal,
        "money" => SqlDbType.Money,
        "smallmoney" => SqlDbType.SmallMoney,
        "float" => SqlDbType.Float,
        "real" => SqlDbType.Real,
        "bit" => SqlDbType.Bit,
        "date" => SqlDbType.Date,
        "datetime" => SqlDbType.DateTime,
        "datetime2" => SqlDbType.DateTime2,
        "smalldatetime" => SqlDbType.SmallDateTime,
        "datetimeoffset" => SqlDbType.DateTimeOffset,
        "time" => SqlDbType.Time,
        "uniqueidentifier" => SqlDbType.UniqueIdentifier,
        "binary" => SqlDbType.Binary,
        "varbinary" => SqlDbType.VarBinary,
        "image" => SqlDbType.Image,
        _ => SqlDbType.NVarChar,
    };

    private static async Task ExecuteRawAsync(SqlConnection conn, SqlTransaction tx, string sql, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void SafeRollback(SqlTransaction tx)
    {
        try { tx.Rollback(); } catch { /* connection may already be broken */ }
    }

    private static object Val(IReadOnlyDictionary<string, object?> values, string col) =>
        values.TryGetValue(col, out var v) && v is not null ? v : DBNull.Value;

    private static bool IsNull(IReadOnlyDictionary<string, object?> values, string col) =>
        !values.TryGetValue(col, out var v) || v is null || v is DBNull;

    private static string Bracket(string identifier) => "[" + identifier.Replace("]", "]]") + "]";
}
