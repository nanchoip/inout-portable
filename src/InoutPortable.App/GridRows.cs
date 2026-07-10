using InoutPortable.Core.Models;

namespace InoutPortable.App;

/// <summary>One row of the per-table summary grid.</summary>
public sealed class SummaryRow
{
    /// <summary>Whether the user wants to import this sheet (editable checkbox).</summary>
    public bool Include { get; set; }
    public required string Sheet { get; init; }
    public required string Table { get; init; }
    public int HeaderRow { get; init; }
    public int Insert { get; init; }
    public int Update { get; init; }
    public int Errors { get; init; }
    public required string Status { get; init; }
    public bool NoKey { get; init; }
    public TableImportPlan? Plan { get; init; }
}

/// <summary>One row of the issues grid.</summary>
public sealed class IssueRow
{
    public required string Location { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
}

/// <summary>One row of the history grid.</summary>
public sealed class HistoryRow
{
    public required string When { get; init; }
    public required string FileName { get; init; }
    public int Inserted { get; init; }
    public int Updated { get; init; }
    public int Skipped { get; init; }
    public required string Result { get; init; }
    public required string Message { get; init; }
}
