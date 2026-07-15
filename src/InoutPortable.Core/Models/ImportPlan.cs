using InoutPortable.Core.Import;

namespace InoutPortable.Core.Models;

public enum RowOperation
{
    Insert,
    Update,
    /// <summary>Row has a validation error and will be skipped (or block the sheet).</summary>
    Error,
}

/// <summary>The planned action for a single Excel row.</summary>
public sealed class RowPlan
{
    public required RowData Row { get; init; }
    public required RowOperation Operation { get; init; }

    /// <summary>Friendly primary-key value(s), for display/diagnostics.</summary>
    public string? KeyDisplay { get; init; }

    /// <summary>
    /// Converted CLR values (ready for SQL parameters) for the mapped + key columns.
    /// Populated for Insert/Update rows; null for Error rows.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? ConvertedValues { get; init; }
}

/// <summary>The full plan for importing one sheet into one table.</summary>
public sealed class TableImportPlan
{
    public required string Sheet { get; init; }
    public required TableMetadata Table { get; init; }

    /// <summary>Effective key columns used to decide insert vs update (PK or user-chosen).</summary>
    public required IReadOnlyList<string> KeyColumns { get; init; }

    /// <summary>Columns from the sheet that map to writable destination columns.</summary>
    public required IReadOnlyList<string> MappedColumns { get; init; }

    /// <summary>1-based Excel row detected/used as the header for this sheet.</summary>
    public int HeaderRowNumber { get; set; } = 1;

    public List<RowPlan> Rows { get; init; } = new();
    public List<ValidationIssue> Issues { get; init; } = new();

    /// <summary>When true the sheet cannot be imported (e.g. missing table, no key, structural errors).</summary>
    public bool IsBlocked { get; set; }

    // --- Client import (CLIENTES view -> __ORGANIZACION + __CLIENTES). Null/false for normal tables. ---

    /// <summary>True when this plan targets the CLIENTES view and must be written via the client writer.</summary>
    public bool IsClientImport { get; set; }

    /// <summary>Sheet column -> destination (organization or client base column). Set for client imports.</summary>
    public IReadOnlyDictionary<string, ClientTarget>? ClientTargets { get; set; }

    /// <summary>Metadata of the __CLIENTES base table (client import only).</summary>
    public TableMetadata? ClientTable { get; set; }

    /// <summary>Metadata of the __ORGANIZACION base table (client import only).</summary>
    public TableMetadata? OrgTable { get; set; }

    public int InsertCount => Rows.Count(r => r.Operation == RowOperation.Insert);
    public int UpdateCount => Rows.Count(r => r.Operation == RowOperation.Update);
    public int ErrorCount => Rows.Count(r => r.Operation == RowOperation.Error);

    public bool HasErrors => IsBlocked || Issues.Any(i => i.Severity == ValidationSeverity.Error);

    /// <summary>The sheet can contribute rows to the import (not structurally blocked, has work to do).</summary>
    public bool IsImportable => !IsBlocked && (InsertCount + UpdateCount) > 0;
}

/// <summary>Aggregated preview over all sheets, shown to the user before confirmation.</summary>
public sealed class ImportPreview
{
    public required string FileName { get; init; }
    public List<TableImportPlan> Tables { get; init; } = new();

    /// <summary>Issues not tied to a specific table (e.g. workbook could not be read).</summary>
    public List<ValidationIssue> GlobalIssues { get; init; } = new();

    public int TotalInserts => Tables.Sum(t => t.InsertCount);
    public int TotalUpdates => Tables.Sum(t => t.UpdateCount);
    public int TotalErrors => Tables.Sum(t => t.ErrorCount)
        + Tables.SelectMany(t => t.Issues).Count(i => i.Severity == ValidationSeverity.Error)
        + GlobalIssues.Count(i => i.Severity == ValidationSeverity.Error);

    public bool CanImportAnything => Tables.Any(t => t.IsImportable);
}
