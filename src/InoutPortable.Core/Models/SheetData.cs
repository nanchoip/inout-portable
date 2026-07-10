namespace InoutPortable.Core.Models;

/// <summary>
/// One worksheet parsed from the workbook. The sheet name is the destination table name,
/// the header row provides the destination column names, and each <see cref="RowData"/>
/// is a data row to upsert.
/// </summary>
public sealed class SheetData
{
    public required string TableName { get; init; }

    /// <summary>Destination column names, in the order they appear in the header row.</summary>
    public required IReadOnlyList<string> Columns { get; init; }

    public required IReadOnlyList<RowData> Rows { get; init; }

    /// <summary>1-based Excel row number of the header (usually 1).</summary>
    public int HeaderRowNumber { get; init; } = 1;
}

/// <summary>A single data row keyed by column name (case-insensitive).</summary>
public sealed class RowData
{
    public RowData(int excelRowNumber, IReadOnlyDictionary<string, CellValue> values)
    {
        ExcelRowNumber = excelRowNumber;
        Values = values;
    }

    /// <summary>1-based Excel row number, used for error reporting.</summary>
    public int ExcelRowNumber { get; }

    public IReadOnlyDictionary<string, CellValue> Values { get; }

    public CellValue Get(string column) =>
        Values.TryGetValue(column, out var v) ? v : CellValue.Empty;
}
