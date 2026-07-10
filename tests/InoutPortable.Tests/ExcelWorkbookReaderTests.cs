using ClosedXML.Excel;
using InoutPortable.Core.Excel;
using InoutPortable.Core.Models;
using static InoutPortable.Tests.TestHelpers;

namespace InoutPortable.Tests;

public class ExcelWorkbookReaderTests
{
    private static MemoryStream BuildWorkbook()
    {
        var wb = new XLWorkbook();

        var ws = wb.Worksheets.Add("Clientes");
        ws.Cell(1, 1).Value = "Id";
        ws.Cell(1, 2).Value = "Nombre";
        ws.Cell(1, 3).Value = "Alta";
        ws.Cell(2, 1).Value = 1;
        ws.Cell(2, 2).Value = "Ana";
        ws.Cell(2, 3).Value = new DateTime(2020, 5, 1);
        ws.Cell(3, 1).Value = 2;
        ws.Cell(3, 2).Value = "Luis";
        // row 4 intentionally blank
        ws.Cell(5, 1).Value = 3;
        ws.Cell(5, 2).Value = "Marta";

        var ws2 = wb.Worksheets.Add("Articulos");
        ws2.Cell(1, 1).Value = "Codigo";
        ws2.Cell(1, 2).Value = "Precio";
        ws2.Cell(2, 1).Value = "A100";
        ws2.Cell(2, 2).Value = 9.99;

        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void Reads_each_sheet_raw()
    {
        using var ms = BuildWorkbook();
        var sheets = new ExcelWorkbookReader().ReadRaw(ms);

        Assert.Equal(2, sheets.Count);
        Assert.Equal("Clientes", sheets[0].Name);
        Assert.Equal("Articulos", sheets[1].Name);
    }

    [Fact]
    public void Interpreter_uses_first_row_as_header_when_table_unknown()
    {
        using var ms = BuildWorkbook();
        var raw = new ExcelWorkbookReader().ReadRaw(ms)[0];

        var interpreted = new SheetInterpreter().Interpret(raw, table: null);

        Assert.Equal(1, interpreted.HeaderRowNumber);
        Assert.Equal(new[] { "Id", "Nombre", "Alta" }, interpreted.Sheet.Columns);
    }

    [Fact]
    public void Interpreter_skips_blank_rows_and_preserves_excel_row_numbers()
    {
        using var ms = BuildWorkbook();
        var raw = new ExcelWorkbookReader().ReadRaw(ms)[0];

        var sheet = new SheetInterpreter().Interpret(raw, table: null).Sheet;

        Assert.Equal(3, sheet.Rows.Count);       // rows 2,3,5 (row 4 blank)
        Assert.Equal(5, sheet.Rows[^1].ExcelRowNumber);
        Assert.True(sheet.Rows[0].Get("Id").IsNumber);
        Assert.True(sheet.Rows[0].Get("Alta").IsDateTime);
    }
}
