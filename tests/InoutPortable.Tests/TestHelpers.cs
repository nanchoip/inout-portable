using InoutPortable.Core.Import;
using InoutPortable.Core.Models;

namespace InoutPortable.Tests;

/// <summary>Shared builders and a fake existence lookup for planner tests.</summary>
internal static class TestHelpers
{
    public static ColumnMetadata Col(string name, string sqlType, SqlTypeCategory cat,
        bool nullable = true, bool identity = false, int? maxLen = null, byte? precision = null, int? scale = null)
        => new()
        {
            Name = name,
            SqlType = sqlType,
            Category = cat,
            IsNullable = nullable,
            IsIdentity = identity,
            MaxLength = maxLen,
            Precision = precision,
            Scale = scale,
        };

    public static TableMetadata Table(string name, IReadOnlyList<ColumnMetadata> cols, params string[] pk)
        => new() { Name = name, Columns = cols, PrimaryKey = pk };

    public static RowData Row(int excelRow, params (string Col, object? Value)[] cells)
    {
        var dict = new Dictionary<string, CellValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var (col, value) in cells)
            dict[col] = new CellValue(value);
        return new RowData(excelRow, dict);
    }

    public static SheetData Sheet(string table, IReadOnlyList<string> columns, params RowData[] rows)
        => new() { TableName = table, Columns = columns, Rows = rows };
}

/// <summary>In-memory <see cref="IExistingKeyLookup"/> that marks a candidate "existing" via a predicate.</summary>
internal sealed class FakeExistingKeyLookup : IExistingKeyLookup
{
    private readonly Func<KeyCandidate, bool> _exists;

    public FakeExistingKeyLookup(Func<KeyCandidate, bool> exists) => _exists = exists;

    public static FakeExistingKeyLookup None => new(_ => false);

    public Task<IReadOnlySet<string>> GetExistingKeysAsync(
        TableMetadata table, IReadOnlyList<string> keyColumns,
        IReadOnlyList<KeyCandidate> candidates, CancellationToken ct = default)
    {
        IReadOnlySet<string> set = new HashSet<string>(
            candidates.Where(_exists).Select(c => c.Normalized));
        return Task.FromResult(set);
    }
}
