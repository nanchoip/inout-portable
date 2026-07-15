using InoutPortable.Core.Database;
using InoutPortable.Core.Excel;
using InoutPortable.Core.Models;

namespace InoutPortable.Core.Import;

/// <summary>
/// Coordinates the full import flow: read the workbook, resolve each sheet to a table, validate and
/// build a preview plan, and (on confirmation) execute the transactional upsert.
/// </summary>
public sealed class ImportOrchestrator
{
    private readonly ConnectionSettings _settings;
    private readonly ExcelWorkbookReader _reader;
    private readonly SheetInterpreter _interpreter;
    private readonly SqlServerMetadataProvider _metadata;
    private readonly ImportPlanner _planner;
    private readonly ClientImportPlanner _clientPlanner;

    public ImportOrchestrator(ConnectionSettings settings)
    {
        _settings = settings;
        _reader = new ExcelWorkbookReader();
        _interpreter = new SheetInterpreter();
        _metadata = new SqlServerMetadataProvider(settings);
        _planner = new ImportPlanner();
        _clientPlanner = new ClientImportPlanner(settings);
    }

    /// <summary>
    /// Reads the workbook and builds a validated preview. <paramref name="keyOverrides"/> lets the
    /// caller supply key columns for sheets whose table has no detectable primary key (keyed by sheet name).
    /// </summary>
    public async Task<ImportPreview> BuildPreviewAsync(
        string filePath,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? keyOverrides = null,
        IReadOnlyDictionary<string, int>? headerOverrides = null,
        CancellationToken ct = default)
    {
        var preview = new ImportPreview { FileName = Path.GetFileName(filePath) };

        IReadOnlyList<RawSheet> rawSheets;
        try
        {
            rawSheets = _reader.ReadRaw(filePath);
        }
        catch (Exception ex)
        {
            preview.GlobalIssues.Add(ValidationIssue.Structural("(archivo)",
                $"No se pudo leer el archivo Excel: {ex.Message}"));
            return preview;
        }

        if (rawSheets.Count == 0)
        {
            preview.GlobalIssues.Add(ValidationIssue.Structural("(archivo)",
                "El archivo no contiene hojas con datos."));
            return preview;
        }

        var lookup = new SqlExistingKeyLookup(_settings);

        foreach (var raw in rawSheets)
        {
            ct.ThrowIfCancellationRequested();

            if (raw.Rows.Count == 0 || raw.Rows.All(r => r.All(c => c.IsEmpty)))
                continue; // empty/blank sheet -> nothing to import

            TableLookup resolved;
            try
            {
                resolved = await _metadata.LookupTableAsync(raw.Name, ct);
            }
            catch (Exception ex)
            {
                preview.GlobalIssues.Add(ValidationIssue.Structural(raw.Name,
                    $"No se pudo consultar la tabla '{raw.Name}': {ex.Message}"));
                continue;
            }

            if (resolved.Status != TableLookupStatus.Found || resolved.Table is null)
            {
                preview.GlobalIssues.Add(ValidationIssue.Structural(raw.Name,
                    resolved.Message ?? $"No se pudo resolver la tabla '{raw.Name}'."));
                continue;
            }

            var table = resolved.Table;

            int? forcedHeader = headerOverrides is not null && headerOverrides.TryGetValue(raw.Name, out var h) ? h : null;
            var interpreted = _interpreter.Interpret(raw, table, forcedHeader);
            if (interpreted.Sheet.Columns.Count == 0)
                continue;

            // CLIENTES is a non-writable view over __CLIENTES + __ORGANIZACION; route it to the
            // dedicated client importer instead of blocking it as a read-only view.
            if (table.Name.Equals("CLIENTES", StringComparison.OrdinalIgnoreCase))
            {
                var cliMeta = (await _metadata.LookupTableAsync("__CLIENTES", ct)).Table;
                var orgMeta = (await _metadata.LookupTableAsync("__ORGANIZACION", ct)).Table;
                if (cliMeta is null || orgMeta is null)
                {
                    preview.GlobalIssues.Add(ValidationIssue.Structural(raw.Name,
                        "No se encontraron las tablas base de clientes (__CLIENTES/__ORGANIZACION) en esta base de datos."));
                    continue;
                }
                var clientPlan = await _clientPlanner.BuildPlanAsync(interpreted.Sheet, table, cliMeta, orgMeta, ct);
                clientPlan.HeaderRowNumber = interpreted.HeaderRowNumber;
                preview.Tables.Add(clientPlan);
                continue;
            }

            IReadOnlyList<string>? manualKey = null;
            keyOverrides?.TryGetValue(raw.Name, out manualKey);
            var keyColumns = KeyResolver.Resolve(table, interpreted.Sheet.Columns, manualKey);

            var plan = await _planner.BuildPlanAsync(interpreted.Sheet, table, keyColumns, lookup, ct);
            plan.HeaderRowNumber = interpreted.HeaderRowNumber;
            preview.Tables.Add(plan);
        }

        return preview;
    }

    public Task<ImportResult> ExecuteAsync(ImportPreview preview, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var executor = new UpsertExecutor(_settings);
        return executor.ExecuteAsync(preview, progress, ct);
    }
}
