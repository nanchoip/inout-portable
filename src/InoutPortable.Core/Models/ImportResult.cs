namespace InoutPortable.Core.Models;

/// <summary>Outcome of importing a single table within the run.</summary>
public sealed class TableImportResult
{
    public required string Sheet { get; init; }
    public required string Table { get; init; }
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int SkippedWithErrors { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>Outcome of a full import run (all sheets), including whether it was rolled back.</summary>
public sealed class ImportResult
{
    public required string FileName { get; init; }
    public DateTime StartedAt { get; init; } = DateTime.Now;
    public DateTime FinishedAt { get; set; }

    public bool Success { get; set; }

    /// <summary>True when an error forced a full rollback (no data was written).</summary>
    public bool RolledBack { get; set; }

    public string? Message { get; set; }

    public List<TableImportResult> Tables { get; init; } = new();

    public int TotalInserted => Tables.Sum(t => t.Inserted);
    public int TotalUpdated => Tables.Sum(t => t.Updated);
    public int TotalSkipped => Tables.Sum(t => t.SkippedWithErrors);
}
