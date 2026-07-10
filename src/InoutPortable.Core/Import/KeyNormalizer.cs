using System.Globalization;
using InoutPortable.Core.Models;

namespace InoutPortable.Core.Import;

/// <summary>
/// Produces a canonical string for a primary-key value so that a value read from Excel and the
/// same value read back from SQL Server compare equal regardless of representation
/// (e.g. <c>1.50</c> vs <c>1.5</c>, trailing spaces in <c>char</c>, or letter case in text keys).
/// Both the Excel side and the database side must be normalized through this class.
/// </summary>
public static class KeyNormalizer
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly char TupleSeparator = (char)1;
    private static readonly string NullMarker = (char)0 + "NULL";

    public static string NormalizeValue(SqlTypeCategory category, object? clr)
    {
        if (clr is null || clr is DBNull)
            return NullMarker;

        return category switch
        {
            SqlTypeCategory.Integer => Convert.ToInt64(clr, Inv).ToString(Inv),
            SqlTypeCategory.Decimal => ToDecimal(clr).ToString("0.############################", Inv),
            SqlTypeCategory.Float => Convert.ToDouble(clr, Inv).ToString("R", Inv),
            SqlTypeCategory.Bit => Convert.ToBoolean(clr) ? "1" : "0",
            SqlTypeCategory.DateTime => ToDateTime(clr).ToString("yyyy-MM-ddTHH:mm:ss.fffffff", Inv),
            SqlTypeCategory.Time => (clr is TimeSpan ts ? ts : TimeSpan.Parse(clr.ToString()!, Inv)).ToString("c"),
            SqlTypeCategory.Guid => (clr is Guid g ? g : Guid.Parse(clr.ToString()!)).ToString("D").ToLowerInvariant(),
            // Text keys: trailing-space and case insensitive to match SQL Server's usual behavior.
            SqlTypeCategory.Text => clr.ToString()!.TrimEnd().ToUpperInvariant(),
            _ => clr.ToString() ?? "",
        };
    }

    public static string NormalizeTuple(IReadOnlyList<SqlTypeCategory> categories, IReadOnlyList<object?> values)
    {
        var parts = new string[values.Count];
        for (int i = 0; i < values.Count; i++)
            parts[i] = NormalizeValue(categories[i], values[i]);
        return string.Join(TupleSeparator, parts);
    }

    private static decimal ToDecimal(object clr) => clr switch
    {
        decimal d => d,
        double db => (decimal)db,
        float f => (decimal)f,
        _ => Convert.ToDecimal(clr, Inv),
    };

    private static DateTime ToDateTime(object clr) => clr switch
    {
        DateTime dt => dt,
        DateTimeOffset dto => dto.DateTime,
        _ => Convert.ToDateTime(clr, Inv),
    };
}
