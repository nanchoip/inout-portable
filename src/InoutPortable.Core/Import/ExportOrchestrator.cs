using InoutPortable.Core.Database;
using InoutPortable.Core.Excel;
using InoutPortable.Core.Models;
using Microsoft.Data.SqlClient;

namespace InoutPortable.Core.Import;

/// <summary>
/// Exports SQL Server tables/views to an .xlsx workbook, one worksheet per table (sheet name = table
/// name, header = columns) — the inverse of the import and in the same format, so files round-trip.
/// </summary>
public sealed class ExportOrchestrator
{
    private readonly ConnectionSettings _settings;
    private readonly SqlServerMetadataProvider _metadata;

    public ExportOrchestrator(ConnectionSettings settings)
    {
        _settings = settings;
        _metadata = new SqlServerMetadataProvider(settings);
    }

    public async Task<ExportResult> ExportAsync(
        IReadOnlyList<string> tableNames,
        string outputPath,
        int? maxRows = null,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var result = new ExportResult { FilePath = outputPath };
        var sheets = new List<SheetExport>();

        await using var conn = new SqlConnection(_settings.BuildConnectionString());
        await conn.OpenAsync(ct);

        foreach (var name in tableNames)
        {
            ct.ThrowIfCancellationRequested();

            TableLookup resolved;
            try
            {
                resolved = await _metadata.LookupTableAsync(name, ct);
            }
            catch (Exception ex)
            {
                result.Tables.Add(new TableExportResult { Table = name, Sheet = name, Success = false, Error = ex.Message });
                continue;
            }

            if (resolved.Status != TableLookupStatus.Found || resolved.Table is null)
            {
                result.Tables.Add(new TableExportResult { Table = name, Sheet = name, Success = false, Error = resolved.Message });
                continue;
            }

            var table = resolved.Table;
            string sheetName = table.Schema.Equals("dbo", StringComparison.OrdinalIgnoreCase) ? table.Name : $"{table.Schema}.{table.Name}";
            progress?.Report($"Exportando {table.FullName}…");

            try
            {
                var (columns, rows) = await ReadTableAsync(conn, table.FullName, maxRows, ct);
                sheets.Add(new SheetExport { Name = sheetName, Columns = columns, Rows = rows });
                result.Tables.Add(new TableExportResult { Table = table.FullName, Sheet = sheetName, RowCount = rows.Count, Success = true });
            }
            catch (Exception ex)
            {
                result.Tables.Add(new TableExportResult { Table = table.FullName, Sheet = sheetName, Success = false, Error = ex.Message });
            }
        }

        if (sheets.Count == 0)
        {
            result.Success = false;
            result.Message = "No se pudo exportar ninguna tabla.";
            result.FinishedAt = DateTime.Now;
            return result;
        }

        progress?.Report("Escribiendo el archivo Excel…");
        new ExcelWorkbookWriter().Write(outputPath, sheets);

        result.Success = true;
        result.Message = $"Exportadas {result.ExportedTables} tabla(s) y {result.TotalRows} fila(s) a '{Path.GetFileName(outputPath)}'.";
        result.FinishedAt = DateTime.Now;
        return result;
    }

    private static async Task<(IReadOnlyList<string> Columns, List<object?[]> Rows)> ReadTableAsync(
        SqlConnection conn, string fullName, int? maxRows, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = maxRows is int n && n > 0
            ? $"SELECT TOP ({n}) * FROM {fullName}"
            : $"SELECT * FROM {fullName}";

        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var columns = new string[reader.FieldCount];
        for (int i = 0; i < reader.FieldCount; i++)
            columns[i] = reader.GetName(i);

        var rows = new List<object?[]>();
        while (await reader.ReadAsync(ct))
        {
            var row = new object?[reader.FieldCount];
            for (int i = 0; i < reader.FieldCount; i++)
                row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }

        return (columns, rows);
    }
}
