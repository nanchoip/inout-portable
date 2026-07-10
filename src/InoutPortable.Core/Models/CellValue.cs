namespace InoutPortable.Core.Models;

/// <summary>
/// A single cell value read from Excel, normalized to a small set of CLR types:
/// <c>null</c>, <see cref="string"/>, <see cref="double"/>, <see cref="bool"/>,
/// <see cref="DateTime"/> or <see cref="TimeSpan"/>.
/// </summary>
public sealed class CellValue
{
    public static readonly CellValue Empty = new(null);

    public CellValue(object? raw)
    {
        Raw = raw is string s ? s : raw;
    }

    /// <summary>The normalized value. Never a boxed empty string is treated as null-ish via <see cref="IsEmpty"/>.</summary>
    public object? Raw { get; }

    /// <summary>True when the cell is blank (null or whitespace-only text).</summary>
    public bool IsEmpty =>
        Raw is null || (Raw is string s && string.IsNullOrWhiteSpace(s));

    public bool IsText => Raw is string;
    public bool IsNumber => Raw is double;
    public bool IsBoolean => Raw is bool;
    public bool IsDateTime => Raw is DateTime;
    public bool IsTimeSpan => Raw is TimeSpan;

    /// <summary>Text form used in previews and log messages.</summary>
    public string AsDisplayString() => Raw switch
    {
        null => string.Empty,
        DateTime dt => dt.TimeOfDay == TimeSpan.Zero
            ? dt.ToString("yyyy-MM-dd")
            : dt.ToString("yyyy-MM-dd HH:mm:ss"),
        double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
        bool b => b ? "1" : "0",
        _ => Raw.ToString() ?? string.Empty,
    };

    public override string ToString() => AsDisplayString();
}
