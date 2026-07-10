using InoutPortable.Core.Models;
using InoutPortable.Core.Validation;

namespace InoutPortable.Core.Import;

/// <summary>
/// Builds a <see cref="TableImportPlan"/> for one sheet: validates structure and cell values,
/// determines each row's primary key, and classifies rows as Insert/Update/Error by consulting
/// an <see cref="IExistingKeyLookup"/>.
/// </summary>
public sealed class ImportPlanner
{
    private readonly TypeConverter _converter;

    public ImportPlanner(TypeConverter? converter = null) => _converter = converter ?? new TypeConverter();

    public async Task<TableImportPlan> BuildPlanAsync(
        SheetData sheet,
        TableMetadata table,
        IReadOnlyList<string> keyColumns,
        IExistingKeyLookup lookup,
        CancellationToken ct = default)
    {
        // --- Column mapping ---
        var mapped = new List<string>();
        var unknown = sheet.Columns.Where(c => !table.HasColumn(c)).ToList();
        var plan = NewPlan(sheet, table, keyColumns, mapped);
        foreach (var c in unknown)
            plan.Issues.Add(ValidationIssue.Structural(sheet.TableName,
                $"La columna '{c}' no existe en la tabla {table.FullName}.", c));

        foreach (var col in sheet.Columns)
        {
            var meta = table.GetColumn(col);
            if (meta is null) continue;
            if (!meta.IsWritable)
                plan.Issues.Add(ValidationIssue.Cell(sheet.TableName, sheet.HeaderRowNumber, col,
                    $"La columna '{col}' es calculada/no editable y se ignorará.", ValidationSeverity.Warning));
            else
                mapped.Add(col);
        }

        // --- Key column checks ---
        if (keyColumns.Count == 0)
        {
            plan.Issues.Add(ValidationIssue.Structural(sheet.TableName,
                $"La tabla {table.FullName} no tiene clave primaria detectable. Seleccione una o varias columnas clave para esta hoja."));
            plan.IsBlocked = true;
        }

        foreach (var k in keyColumns)
        {
            if (!table.HasColumn(k))
            {
                plan.Issues.Add(ValidationIssue.Structural(sheet.TableName,
                    $"La columna clave '{k}' no existe en la tabla {table.FullName}.", k));
                plan.IsBlocked = true;
            }
            else if (!sheet.Columns.Contains(k, StringComparer.OrdinalIgnoreCase))
            {
                plan.Issues.Add(ValidationIssue.Structural(sheet.TableName,
                    $"La columna clave '{k}' no está presente en el Excel; es necesaria para localizar las filas.", k));
                plan.IsBlocked = true;
            }
        }

        if (unknown.Count > 0)
            plan.IsBlocked = true;

        // Warn about non-nullable columns missing from the sheet (only relevant for inserts).
        foreach (var col in table.Columns)
        {
            if (col.IsWritable && !col.IsNullable && !col.IsIdentity
                && !sheet.Columns.Contains(col.Name, StringComparer.OrdinalIgnoreCase))
            {
                plan.Issues.Add(ValidationIssue.Cell(sheet.TableName, sheet.HeaderRowNumber, col.Name,
                    $"La columna obligatoria '{col.Name}' no está en el Excel. Las nuevas filas usarán su valor por defecto o fallarán si no lo tiene.",
                    ValidationSeverity.Warning));
            }
        }

        if (plan.IsBlocked)
            return plan;

        // --- Row-by-row conversion, validation and key extraction ---
        var keyCategories = keyColumns.Select(k => table.GetColumn(k)!.Category).ToList();
        var columnsToConvert = mapped.Union(keyColumns, StringComparer.OrdinalIgnoreCase).ToList();
        var seenKeys = new HashSet<string>();
        var intermediates = new List<RowIntermediate>();
        var candidates = new List<KeyCandidate>();

        foreach (var row in sheet.Rows)
        {
            ct.ThrowIfCancellationRequested();
            bool valid = true;
            var converted = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            foreach (var col in columnsToConvert)
            {
                var meta = table.GetColumn(col)!;
                var result = _converter.Convert(meta, row.Get(col));
                if (!result.Ok)
                {
                    plan.Issues.Add(ValidationIssue.Cell(sheet.TableName, row.ExcelRowNumber, col, result.Error!));
                    valid = false;
                    continue;
                }
                converted[col] = result.Value;

                // Required check for writable, mapped, non-identity, non-nullable columns.
                bool isNull = result.Value is null || result.Value is DBNull;
                if (isNull && mapped.Contains(col, StringComparer.OrdinalIgnoreCase)
                    && !meta.IsNullable && !meta.IsIdentity)
                {
                    plan.Issues.Add(ValidationIssue.Cell(sheet.TableName, row.ExcelRowNumber, col,
                        $"La columna '{col}' es obligatoria y no puede estar vacía."));
                    valid = false;
                }
            }

            // Key extraction
            var keyValues = new List<object?>(keyColumns.Count);
            bool keyOk = true;
            foreach (var k in keyColumns)
            {
                converted.TryGetValue(k, out var kv);
                if (kv is null || kv is DBNull)
                {
                    if (valid) // avoid duplicate noise when the cell already errored
                        plan.Issues.Add(ValidationIssue.Cell(sheet.TableName, row.ExcelRowNumber, k,
                            $"La columna clave '{k}' no puede estar vacía."));
                    keyOk = false;
                }
                keyValues.Add(kv);
            }

            string keyDisplay = string.Join(", ", keyColumns.Select(k => row.Get(k).AsDisplayString()));

            if (!valid || !keyOk)
            {
                intermediates.Add(new RowIntermediate(row, null, null, keyDisplay, false));
                continue;
            }

            string normalized = KeyNormalizer.NormalizeTuple(keyCategories, keyValues);
            if (!seenKeys.Add(normalized))
            {
                plan.Issues.Add(ValidationIssue.Cell(sheet.TableName, row.ExcelRowNumber, string.Join("+", keyColumns),
                    $"Clave duplicada en el Excel ({keyDisplay}); solo se puede importar una vez."));
                intermediates.Add(new RowIntermediate(row, null, null, keyDisplay, false));
                continue;
            }

            intermediates.Add(new RowIntermediate(row, converted, normalized, keyDisplay, true));
            candidates.Add(new KeyCandidate(normalized, keyValues));
        }

        // --- Existence lookup + classification ---
        var existing = await lookup.GetExistingKeysAsync(table, keyColumns, candidates, ct);

        foreach (var it in intermediates)
        {
            if (!it.Valid)
            {
                plan.Rows.Add(new RowPlan { Row = it.Row, Operation = RowOperation.Error, KeyDisplay = it.KeyDisplay });
                continue;
            }

            var op = existing.Contains(it.Normalized!) ? RowOperation.Update : RowOperation.Insert;
            plan.Rows.Add(new RowPlan
            {
                Row = it.Row,
                Operation = op,
                KeyDisplay = it.KeyDisplay,
                ConvertedValues = it.Converted,
            });
        }

        return plan;
    }

    private static TableImportPlan NewPlan(SheetData sheet, TableMetadata table,
        IReadOnlyList<string> keyColumns, IReadOnlyList<string> mapped) => new()
    {
        Sheet = sheet.TableName,
        Table = table,
        KeyColumns = keyColumns,
        MappedColumns = mapped,
    };

    private sealed record RowIntermediate(
        RowData Row,
        IReadOnlyDictionary<string, object?>? Converted,
        string? Normalized,
        string KeyDisplay,
        bool Valid);
}
