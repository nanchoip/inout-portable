namespace InoutPortable.Core.Models;

/// <summary>
/// A worksheet as read from the file with no header assumptions: every row, every cell.
/// Header-row detection happens later (see SheetInterpreter), because a3ERP templates sometimes
/// place a human-readable label row above the real field-code row.
/// </summary>
public sealed class RawSheet
{
    public required string Name { get; init; }

    /// <summary>All rows (1-based Excel row = index + 1). Each row is the list of cell values.</summary>
    public required IReadOnlyList<IReadOnlyList<CellValue>> Rows { get; init; }

    public int RowCount => Rows.Count;
}
