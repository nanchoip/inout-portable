using System.ComponentModel;
using InoutPortable.Core.Models;

namespace InoutPortable.App;

/// <summary>A selectable table/view in the export list.</summary>
public sealed class ExportTableItem : INotifyPropertyChanged
{
    public required string Name { get; init; }

    private bool _checked;
    public bool Checked
    {
        get => _checked;
        set { if (_checked != value) { _checked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Checked))); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

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
