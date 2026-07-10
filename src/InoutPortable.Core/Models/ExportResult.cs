namespace InoutPortable.Core.Models;

public sealed class TableExportResult
{
    public required string Table { get; init; }
    public required string Sheet { get; init; }
    public int RowCount { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}

public sealed class ExportResult
{
    public required string FilePath { get; init; }
    public DateTime StartedAt { get; init; } = DateTime.Now;
    public DateTime FinishedAt { get; set; }
    public bool Success { get; set; }
    public string? Message { get; set; }
    public List<TableExportResult> Tables { get; init; } = new();

    public int TotalRows => Tables.Where(t => t.Success).Sum(t => t.RowCount);
    public int ExportedTables => Tables.Count(t => t.Success);
}
