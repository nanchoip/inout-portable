using InoutPortable.Core.Models;

namespace InoutPortable.Core.Import;

/// <summary>
/// Decides which columns to use as the match key for a sheet, in priority order:
/// 1) a manual override chosen by the user; 2) the primary key, if all its columns are in the sheet;
/// 3) the smallest unique key whose columns are all in the sheet (handles surrogate-PK tables like
/// a3ERP's CUENTAS, keyed in practice by [PLACON, CUENTA]); otherwise the PK is returned so the
/// planner blocks the sheet and the user is asked to pick a key.
/// </summary>
public static class KeyResolver
{
    public static IReadOnlyList<string> Resolve(
        TableMetadata table,
        IReadOnlyList<string> sheetColumns,
        IReadOnlyList<string>? manualOverride)
    {
        if (manualOverride is { Count: > 0 })
            return manualOverride;

        bool Present(IReadOnlyList<string> cols) =>
            cols.Count > 0 && cols.All(c => sheetColumns.Contains(c, StringComparer.OrdinalIgnoreCase));

        if (Present(table.PrimaryKey))
            return table.PrimaryKey;

        var alternative = table.UniqueKeys
            .Where(Present)
            .OrderBy(k => k.Count)
            .FirstOrDefault();

        return alternative ?? table.PrimaryKey;
    }
}
