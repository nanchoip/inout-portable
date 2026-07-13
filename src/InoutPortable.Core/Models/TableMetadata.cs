namespace InoutPortable.Core.Models;

/// <summary>Broad classification of a SQL Server column type, used to drive value validation/conversion.</summary>
public enum SqlTypeCategory
{
    Text,        // char, varchar, nchar, nvarchar, text, ntext
    Integer,     // tinyint, smallint, int, bigint
    Decimal,     // decimal, numeric, money, smallmoney
    Float,       // float, real
    Bit,         // bit
    DateTime,    // date, datetime, datetime2, smalldatetime, datetimeoffset
    Time,        // time
    Guid,        // uniqueidentifier
    Binary,      // binary, varbinary, image
    Other,
}

/// <summary>Metadata for a single destination column, resolved from SQL Server.</summary>
public sealed class ColumnMetadata
{
    public required string Name { get; init; }
    public required string SqlType { get; init; }
    public SqlTypeCategory Category { get; init; }

    /// <summary>Max character length for text types. <c>-1</c> means MAX; <c>null</c> for non-text.</summary>
    public int? MaxLength { get; init; }

    public byte? Precision { get; init; }
    public int? Scale { get; init; }
    public bool IsNullable { get; init; }
    public bool IsIdentity { get; init; }

    /// <summary>Computed columns (and rowversion/timestamp) cannot be written to.</summary>
    public bool IsComputed { get; init; }

    public int OrdinalPosition { get; init; }

    /// <summary>Computed columns and rowversion/timestamp columns cannot be inserted/updated.</summary>
    public bool IsWritable => !IsComputed
        && !string.Equals(SqlType, "timestamp", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(SqlType, "rowversion", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Metadata for a destination table, resolved from SQL Server.</summary>
public sealed class TableMetadata
{
    public string Schema { get; init; } = "dbo";
    public required string Name { get; init; }
    public required IReadOnlyList<ColumnMetadata> Columns { get; init; }

    /// <summary>Primary key column names in key order. Empty when the table has no detectable PK.</summary>
    public required IReadOnlyList<string> PrimaryKey { get; init; }

    /// <summary>Other unique keys (unique indexes/constraints), each an ordered column list. Excludes the PK.</summary>
    public IReadOnlyList<IReadOnlyList<string>> UniqueKeys { get; init; } = Array.Empty<IReadOnlyList<string>>();

    /// <summary>True when the destination object is a view rather than a base table.</summary>
    public bool IsView { get; init; }

    /// <summary>False when the object cannot be written to (e.g. a non-updatable multi-table a3ERP view).</summary>
    public bool IsWritableTarget { get; init; } = true;

    /// <summary>Explains why the object is not writable, shown to the user when <see cref="IsWritableTarget"/> is false.</summary>
    public string? NotWritableReason { get; init; }

    /// <summary>Friendly sheet name that was mapped to this object (e.g. "Clientes" -> "CLIENTES"), if any.</summary>
    public string? MatchedVia { get; init; }

    public bool HasPrimaryKey => PrimaryKey.Count > 0;

    public string FullName => $"[{Schema.Replace("]", "]]")}].[{Name.Replace("]", "]]")}]";

    private Dictionary<string, ColumnMetadata>? _byName;

    public ColumnMetadata? GetColumn(string name)
    {
        _byName ??= Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        return _byName.TryGetValue(name, out var c) ? c : null;
    }

    public bool HasColumn(string name) => GetColumn(name) is not null;
}
