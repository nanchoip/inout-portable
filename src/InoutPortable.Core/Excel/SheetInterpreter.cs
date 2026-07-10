using InoutPortable.Core.Models;

namespace InoutPortable.Core.Excel;

/// <summary>Result of turning a <see cref="RawSheet"/> into a header + data <see cref="SheetData"/>.</summary>
public sealed class InterpretedSheet
{
    public required SheetData Sheet { get; init; }

    /// <summary>1-based Excel row chosen as the header.</summary>
    public int HeaderRowNumber { get; init; }

    /// <summary>How many header cells matched a real column of the target table (0 if unknown table).</summary>
    public int MatchedColumns { get; init; }

    public int HeaderColumnCount { get; init; }
}

/// <summary>
/// Decides which row of a raw sheet is the header and builds the <see cref="SheetData"/>.
/// a3ERP templates sometimes have a Spanish label row above the real field-code row, so when the
/// target table is known we pick the row whose cells best match the table's actual column names.
/// </summary>
public sealed class SheetInterpreter
{
    private const int MaxHeaderScanRows = 8;

    public InterpretedSheet Interpret(RawSheet raw, TableMetadata? table, int? forcedHeaderRow1Based = null)
    {
        int headerIndex = ChooseHeaderIndex(raw, table, forcedHeaderRow1Based);

        var headerRow = headerIndex >= 0 && headerIndex < raw.Rows.Count
            ? raw.Rows[headerIndex]
            : (IReadOnlyList<CellValue>)Array.Empty<CellValue>();

        int colCount = LastNonEmptyIndex(headerRow) + 1; // trim trailing empty header cells
        var columns = BuildColumnNames(headerRow, colCount);

        var rows = new List<RowData>();
        for (int r = headerIndex + 1; r < raw.Rows.Count; r++)
        {
            var rawRow = raw.Rows[r];
            var values = new Dictionary<string, CellValue>(StringComparer.OrdinalIgnoreCase);
            bool anyValue = false;

            for (int c = 0; c < columns.Count; c++)
            {
                var cell = c < rawRow.Count ? rawRow[c] : CellValue.Empty;
                values[columns[c]] = cell;
                if (!cell.IsEmpty) anyValue = true;
            }

            if (anyValue)
                rows.Add(new RowData(r + 1, values)); // +1 => 1-based Excel row
        }

        var sheet = new SheetData
        {
            TableName = raw.Name,
            Columns = columns,
            Rows = rows,
            HeaderRowNumber = headerIndex + 1,
        };

        return new InterpretedSheet
        {
            Sheet = sheet,
            HeaderRowNumber = headerIndex + 1,
            MatchedColumns = table is null ? 0 : MatchScore(headerRow, colCount, table),
            HeaderColumnCount = colCount,
        };
    }

    private static int ChooseHeaderIndex(RawSheet raw, TableMetadata? table, int? forced)
    {
        if (raw.Rows.Count == 0) return -1;

        if (forced is int f)
            return Math.Clamp(f - 1, 0, raw.Rows.Count - 1);

        int limit = Math.Min(raw.Rows.Count, MaxHeaderScanRows);

        if (table is not null)
        {
            int bestIdx = -1, bestScore = 0;
            for (int i = 0; i < limit; i++)
            {
                int cols = LastNonEmptyIndex(raw.Rows[i]) + 1;
                int score = MatchScore(raw.Rows[i], cols, table);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIdx = i;
                }
            }
            if (bestIdx >= 0) return bestIdx;
        }

        return FirstNonEmptyIndex(raw.Rows);
    }

    private static int MatchScore(IReadOnlyList<CellValue> row, int colCount, TableMetadata table)
    {
        int score = 0;
        for (int c = 0; c < colCount && c < row.Count; c++)
        {
            var text = row[c].AsDisplayString().Trim();
            if (text.Length > 0 && table.HasColumn(text))
                score++;
        }
        return score;
    }

    private static IReadOnlyList<string> BuildColumnNames(IReadOnlyList<CellValue> headerRow, int colCount)
    {
        var columns = new List<string>(colCount);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int c = 0; c < colCount; c++)
        {
            string name = c < headerRow.Count ? headerRow[c].AsDisplayString().Trim() : "";
            if (name.Length == 0 || seen.Contains(name))
                name = $"(Columna {ColumnLetter(c)})";
            seen.Add(name);
            columns.Add(name);
        }
        return columns;
    }

    private static int FirstNonEmptyIndex(IReadOnlyList<IReadOnlyList<CellValue>> rows)
    {
        for (int i = 0; i < rows.Count; i++)
            if (rows[i].Any(c => !c.IsEmpty))
                return i;
        return 0;
    }

    private static int LastNonEmptyIndex(IReadOnlyList<CellValue> row)
    {
        int last = -1;
        for (int i = 0; i < row.Count; i++)
            if (!row[i].IsEmpty) last = i;
        return last;
    }

    /// <summary>0-based column index to Excel column letters (0 -> A, 26 -> AA).</summary>
    public static string ColumnLetter(int index)
    {
        var sb = new System.Text.StringBuilder();
        index++;
        while (index > 0)
        {
            int rem = (index - 1) % 26;
            sb.Insert(0, (char)('A' + rem));
            index = (index - 1) / 26;
        }
        return sb.ToString();
    }
}
