using InoutPortable.Core.Import;
using InoutPortable.Core.Models;

namespace InoutPortable.Tests;

public class KeyNormalizerTests
{
    [Fact]
    public void Decimal_scale_differences_normalize_equal()
    {
        var a = KeyNormalizer.NormalizeValue(SqlTypeCategory.Decimal, 1.5m);
        var b = KeyNormalizer.NormalizeValue(SqlTypeCategory.Decimal, 1.50m);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Integer_from_int_and_long_normalize_equal()
    {
        var a = KeyNormalizer.NormalizeValue(SqlTypeCategory.Integer, 7);
        var b = KeyNormalizer.NormalizeValue(SqlTypeCategory.Integer, 7L);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Text_keys_are_case_and_trailing_space_insensitive()
    {
        var a = KeyNormalizer.NormalizeValue(SqlTypeCategory.Text, "abc");
        var b = KeyNormalizer.NormalizeValue(SqlTypeCategory.Text, "ABC   ");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Different_values_normalize_differently()
    {
        var a = KeyNormalizer.NormalizeValue(SqlTypeCategory.Integer, 1);
        var b = KeyNormalizer.NormalizeValue(SqlTypeCategory.Integer, 2);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Composite_tuple_distinguishes_column_order()
    {
        var cats = new[] { SqlTypeCategory.Integer, SqlTypeCategory.Text };
        var ab = KeyNormalizer.NormalizeTuple(cats, new object?[] { 1, "b" });
        var ba = KeyNormalizer.NormalizeTuple(cats, new object?[] { 2, "b" });
        Assert.NotEqual(ab, ba);
    }
}
