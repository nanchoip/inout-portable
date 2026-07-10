using System.Globalization;
using InoutPortable.Core.Models;

namespace InoutPortable.Core.Validation;

/// <summary>Result of converting an Excel cell to the CLR value expected by a SQL Server column.</summary>
public sealed record ConversionResult(bool Ok, object? Value, string? Error)
{
    public static ConversionResult Success(object? value) => new(true, value, null);
    public static ConversionResult Fail(string error) => new(false, null, error);
    public static readonly ConversionResult Null = new(true, DBNull.Value, null);
}

/// <summary>
/// Converts a normalized <see cref="CellValue"/> into the CLR value a SQL parameter needs for a
/// given column, applying type/range/length rules. Empty cells convert to <see cref="DBNull"/>;
/// required-ness is enforced separately by the validator.
/// </summary>
public sealed class TypeConverter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    public ConversionResult Convert(ColumnMetadata col, CellValue cell)
    {
        if (cell.IsEmpty)
            return ConversionResult.Null;

        return col.Category switch
        {
            SqlTypeCategory.Text => ConvertText(col, cell),
            SqlTypeCategory.Integer => ConvertInteger(col, cell),
            SqlTypeCategory.Decimal => ConvertDecimal(col, cell),
            SqlTypeCategory.Float => ConvertFloat(cell),
            SqlTypeCategory.Bit => ConvertBit(cell),
            SqlTypeCategory.DateTime => ConvertDateTime(col, cell),
            SqlTypeCategory.Time => ConvertTime(cell),
            SqlTypeCategory.Guid => ConvertGuid(cell),
            SqlTypeCategory.Binary => ConversionResult.Fail("Los datos binarios no se pueden importar desde Excel."),
            _ => ConversionResult.Success(cell.AsDisplayString()),
        };
    }

    private static ConversionResult ConvertText(ColumnMetadata col, CellValue cell)
    {
        string text = cell.AsDisplayString();
        if (col.MaxLength is > 0 && text.Length > col.MaxLength)
            return ConversionResult.Fail(
                $"El texto tiene {text.Length} caracteres pero la columna admite un máximo de {col.MaxLength}.");
        return ConversionResult.Success(text);
    }

    private static ConversionResult ConvertInteger(ColumnMetadata col, CellValue cell)
    {
        long value;
        switch (cell.Raw)
        {
            case double d:
                if (Math.Abs(d - Math.Round(d)) > 1e-9)
                    return ConversionResult.Fail($"El valor '{cell.AsDisplayString()}' no es un número entero.");
                value = (long)Math.Round(d);
                break;
            case bool b:
                value = b ? 1 : 0;
                break;
            case string s when TryParseLong(s, out var l):
                value = l;
                break;
            default:
                return ConversionResult.Fail($"El valor '{cell.AsDisplayString()}' no es un número entero válido.");
        }

        var (min, max) = IntegerRange(col.SqlType);
        if (value < min || value > max)
            return ConversionResult.Fail($"El valor {value} está fuera del rango de '{col.SqlType}' ({min}..{max}).");

        return ConversionResult.Success(value);
    }

    private static ConversionResult ConvertDecimal(ColumnMetadata col, CellValue cell)
    {
        decimal value;
        switch (cell.Raw)
        {
            case double d:
                value = (decimal)d;
                break;
            case bool b:
                value = b ? 1 : 0;
                break;
            case string s when TryParseDecimal(s, out var m):
                value = m;
                break;
            default:
                return ConversionResult.Fail($"El valor '{cell.AsDisplayString()}' no es un número decimal válido.");
        }

        if (col.Precision is byte p && col.Scale is int sc)
        {
            var integerDigits = p - sc;
            var absTruncated = Math.Truncate(Math.Abs(value));
            if (absTruncated != 0 && absTruncated.ToString(Inv).Length > integerDigits)
                return ConversionResult.Fail(
                    $"El número {value} excede la precisión de la columna ({col.SqlType}({p},{sc})).");
        }

        return ConversionResult.Success(value);
    }

    private static ConversionResult ConvertFloat(CellValue cell)
    {
        return cell.Raw switch
        {
            double d => ConversionResult.Success(d),
            bool b => ConversionResult.Success(b ? 1d : 0d),
            string s when TryParseDouble(s, out var v) => ConversionResult.Success(v),
            _ => ConversionResult.Fail($"El valor '{cell.AsDisplayString()}' no es un número válido."),
        };
    }

    private static ConversionResult ConvertBit(CellValue cell)
    {
        switch (cell.Raw)
        {
            case bool b:
                return ConversionResult.Success(b);
            case double d when d is 0 or 1:
                return ConversionResult.Success(d == 1);
            case string s:
                var t = s.Trim().ToLowerInvariant();
                return t switch
                {
                    "1" or "true" or "sí" or "si" or "s" or "yes" or "y" or "verdadero" => ConversionResult.Success(true),
                    "0" or "false" or "no" or "n" or "falso" => ConversionResult.Success(false),
                    _ => ConversionResult.Fail($"El valor '{s}' no es un booleano (use 0/1, Sí/No, True/False)."),
                };
            default:
                return ConversionResult.Fail($"El valor '{cell.AsDisplayString()}' no es un valor de bit válido (0/1).");
        }
    }

    private static ConversionResult ConvertDateTime(ColumnMetadata col, CellValue cell)
    {
        DateTime dt;
        switch (cell.Raw)
        {
            case DateTime d:
                dt = d;
                break;
            case double serial:
                try { dt = DateTime.FromOADate(serial); }
                catch { return ConversionResult.Fail($"El valor '{cell.AsDisplayString()}' no es una fecha válida."); }
                break;
            case string s when TryParseDate(s, out var parsed):
                dt = parsed;
                break;
            default:
                return ConversionResult.Fail($"El valor '{cell.AsDisplayString()}' no es una fecha válida.");
        }

        // Range checks for the narrower date types.
        if (col.SqlType.Equals("datetime", StringComparison.OrdinalIgnoreCase)
            && (dt < new DateTime(1753, 1, 1) || dt > new DateTime(9999, 12, 31)))
            return ConversionResult.Fail("La fecha está fuera del rango admitido por 'datetime' (1753-9999).");

        if (col.SqlType.Equals("smalldatetime", StringComparison.OrdinalIgnoreCase)
            && (dt < new DateTime(1900, 1, 1) || dt > new DateTime(2079, 6, 6)))
            return ConversionResult.Fail("La fecha está fuera del rango admitido por 'smalldatetime' (1900-2079).");

        if (col.SqlType.Equals("date", StringComparison.OrdinalIgnoreCase))
            dt = dt.Date;

        return ConversionResult.Success(dt);
    }

    private static ConversionResult ConvertTime(CellValue cell)
    {
        return cell.Raw switch
        {
            TimeSpan ts => ConversionResult.Success(ts),
            DateTime d => ConversionResult.Success(d.TimeOfDay),
            string s when TimeSpan.TryParse(s, Inv, out var v) => ConversionResult.Success(v),
            _ => ConversionResult.Fail($"El valor '{cell.AsDisplayString()}' no es una hora válida."),
        };
    }

    private static ConversionResult ConvertGuid(CellValue cell)
    {
        return cell.Raw is string s && Guid.TryParse(s.Trim(), out var g)
            ? ConversionResult.Success(g)
            : ConversionResult.Fail($"El valor '{cell.AsDisplayString()}' no es un identificador único (GUID) válido.");
    }

    // --- parsing helpers (invariant first, Spanish culture as fallback) ---

    private static bool TryParseLong(string s, out long value) =>
        long.TryParse(s.Trim(), NumberStyles.Integer, Inv, out value)
        || long.TryParse(s.Trim(), NumberStyles.Integer, Es, out value);

    private static bool TryParseDecimal(string s, out decimal value) =>
        decimal.TryParse(s.Trim(), NumberStyles.Number, Inv, out value)
        || decimal.TryParse(s.Trim(), NumberStyles.Number, Es, out value);

    private static bool TryParseDouble(string s, out double value) =>
        double.TryParse(s.Trim(), NumberStyles.Float | NumberStyles.AllowThousands, Inv, out value)
        || double.TryParse(s.Trim(), NumberStyles.Float | NumberStyles.AllowThousands, Es, out value);

    private static bool TryParseDate(string s, out DateTime value) =>
        DateTime.TryParse(s.Trim(), Inv, DateTimeStyles.None, out value)
        || DateTime.TryParse(s.Trim(), Es, DateTimeStyles.None, out value);

    private static (long Min, long Max) IntegerRange(string sqlType) => sqlType.ToLowerInvariant() switch
    {
        "tinyint" => (0, 255),
        "smallint" => (short.MinValue, short.MaxValue),
        "int" => (int.MinValue, int.MaxValue),
        "bigint" => (long.MinValue, long.MaxValue),
        _ => (long.MinValue, long.MaxValue),
    };
}
