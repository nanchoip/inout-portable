using ClosedXML.Excel;

namespace InoutPortable.Core.Excel;

/// <summary>One worksheet to write: a name, the column headers, and the data rows.</summary>
public sealed class SheetExport
{
    public required string Name { get; init; }
    public required IReadOnlyList<string> Columns { get; init; }
    public required IEnumerable<object?[]> Rows { get; init; }
}

/// <summary>
/// Writes an .xlsx workbook following the same convention the importer reads: one worksheet per table,
/// the first row is the column names, and the rest are data. Cell types are preserved so the file can
/// be re-imported (round-trip).
/// </summary>
public sealed class ExcelWorkbookWriter
{
    /// <summary>Writes the sheets to <paramref name="path"/>. Returns the number of data rows per sheet.</summary>
    public IReadOnlyDictionary<string, int> Write(string path, IEnumerable<SheetExport> sheets)
    {
        using var wb = new XLWorkbook();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var counts = new Dictionary<string, int>();

        foreach (var sheet in sheets)
        {
            var ws = wb.Worksheets.Add(UniqueSheetName(sheet.Name, usedNames));

            for (int c = 0; c < sheet.Columns.Count; c++)
            {
                var cell = ws.Cell(1, c + 1);
                cell.Value = sheet.Columns[c];
                cell.Style.Font.Bold = true;
            }

            int row = 2;
            foreach (var data in sheet.Rows)
            {
                for (int c = 0; c < sheet.Columns.Count && c < data.Length; c++)
                    SetCell(ws.Cell(row, c + 1), data[c]);
                row++;
            }

            ws.SheetView.FreezeRows(1);
            counts[sheet.Name] = row - 2;
        }

        // Ensure at least one sheet so the file is valid.
        if (wb.Worksheets.Count == 0)
            wb.Worksheets.Add("Sin datos");

        wb.SaveAs(path);
        return counts;
    }

    private static void SetCell(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null or DBNull:
                return; // blank
            case string s:
                cell.Value = s;
                break;
            case bool b:
                cell.Value = b;
                break;
            case DateTime dt:
                cell.Value = dt;
                break;
            case TimeSpan ts:
                cell.Value = ts;
                break;
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                cell.Value = Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
                break;
            case decimal m:
                cell.Value = m;
                break;
            case float or double:
                cell.Value = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
                break;
            case Guid g:
                cell.Value = g.ToString();
                break;
            case byte[]:
                return; // binary/image data is not representable in a cell -> leave blank
            default:
                cell.Value = value.ToString();
                break;
        }
    }

    /// <summary>Excel sheet names: max 31 chars, no []:*?/\, unique.</summary>
    private static string UniqueSheetName(string name, HashSet<string> used)
    {
        var clean = new string((name ?? "Hoja").Select(ch => "[]:*?/\\".Contains(ch) ? '_' : ch).ToArray()).Trim();
        if (clean.Length == 0) clean = "Hoja";
        if (clean.Length > 31) clean = clean[..31];

        var candidate = clean;
        int i = 1;
        while (used.Contains(candidate))
        {
            var suffix = "_" + (++i);
            candidate = (clean.Length + suffix.Length > 31 ? clean[..(31 - suffix.Length)] : clean) + suffix;
        }
        used.Add(candidate);
        return candidate;
    }
}
