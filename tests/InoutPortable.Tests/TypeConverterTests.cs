using InoutPortable.Core.Models;
using InoutPortable.Core.Validation;
using static InoutPortable.Tests.TestHelpers;

namespace InoutPortable.Tests;

public class TypeConverterTests
{
    private readonly TypeConverter _c = new();

    [Fact]
    public void Integer_from_whole_double_ok()
    {
        var r = _c.Convert(Col("Id", "int", SqlTypeCategory.Integer), new CellValue(42d));
        Assert.True(r.Ok);
        Assert.Equal(42L, r.Value);
    }

    [Fact]
    public void Integer_from_fractional_double_fails()
    {
        var r = _c.Convert(Col("Id", "int", SqlTypeCategory.Integer), new CellValue(42.5d));
        Assert.False(r.Ok);
    }

    [Fact]
    public void Tinyint_out_of_range_fails()
    {
        var r = _c.Convert(Col("B", "tinyint", SqlTypeCategory.Integer), new CellValue(300d));
        Assert.False(r.Ok);
    }

    [Fact]
    public void Varchar_over_maxlength_fails()
    {
        var col = Col("Nombre", "varchar", SqlTypeCategory.Text, maxLen: 3);
        var r = _c.Convert(col, new CellValue("abcd"));
        Assert.False(r.Ok);
    }

    [Fact]
    public void Varchar_within_maxlength_ok()
    {
        var col = Col("Nombre", "varchar", SqlTypeCategory.Text, maxLen: 5);
        var r = _c.Convert(col, new CellValue("abc"));
        Assert.True(r.Ok);
        Assert.Equal("abc", r.Value);
    }

    [Fact]
    public void Date_from_excel_serial_number_parses()
    {
        // 2019-01-01 as OLE Automation date serial.
        double serial = new DateTime(2019, 1, 1).ToOADate();
        var r = _c.Convert(Col("F", "datetime", SqlTypeCategory.DateTime), new CellValue(serial));
        Assert.True(r.Ok);
        Assert.Equal(new DateTime(2019, 1, 1), r.Value);
    }

    [Fact]
    public void Datetime_before_1753_fails()
    {
        var r = _c.Convert(Col("F", "datetime", SqlTypeCategory.DateTime), new CellValue(new DateTime(1700, 1, 1)));
        Assert.False(r.Ok);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("Sí", true)]
    [InlineData("no", false)]
    [InlineData("0", false)]
    public void Bit_parses_common_forms(string input, bool expected)
    {
        var r = _c.Convert(Col("Activo", "bit", SqlTypeCategory.Bit), new CellValue(input));
        Assert.True(r.Ok);
        Assert.Equal(expected, r.Value);
    }

    [Fact]
    public void Decimal_exceeding_precision_fails()
    {
        var col = Col("Precio", "decimal", SqlTypeCategory.Decimal, precision: 4, scale: 2); // max 99.99
        var r = _c.Convert(col, new CellValue(12345.67d));
        Assert.False(r.Ok);
    }

    [Fact]
    public void Empty_cell_converts_to_dbnull()
    {
        var r = _c.Convert(Col("X", "int", SqlTypeCategory.Integer), CellValue.Empty);
        Assert.True(r.Ok);
        Assert.Equal(DBNull.Value, r.Value);
    }

    [Fact]
    public void Guid_invalid_fails()
    {
        var r = _c.Convert(Col("G", "uniqueidentifier", SqlTypeCategory.Guid), new CellValue("not-a-guid"));
        Assert.False(r.Ok);
    }
}
