using ClosedXML.Excel;
using InoutPortable.Core.Excel;
using InoutPortable.Core.Models;
using static InoutPortable.Tests.TestHelpers;

namespace InoutPortable.Tests;

public class SheetInterpreterTests
{
    // Mirrors real a3ERP templates: a Spanish label row on top of the real field-code row.
    private static RawSheet TwoHeaderRowSheet()
    {
        var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("ARTICULO");
        // Row 1: human labels
        ws.Cell(1, 1).Value = "Cód. artículo";
        ws.Cell(1, 2).Value = "Descripción artículo";
        ws.Cell(1, 3).Value = "Precio venta";
        // Row 2: real a3ERP field codes (match the table columns)
        ws.Cell(2, 1).Value = "CODART";
        ws.Cell(2, 2).Value = "DESCART";
        ws.Cell(2, 3).Value = "PRCVENTA";
        // Data
        ws.Cell(3, 1).Value = "A100";
        ws.Cell(3, 2).Value = "Bicicleta";
        ws.Cell(3, 3).Value = 864.5;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return new ExcelWorkbookReader().ReadRaw(ms)[0];
    }

    private static TableMetadata ArticuloTable() => Table("ARTICULO", new[]
    {
        Col("CODART", "nvarchar", SqlTypeCategory.Text, nullable: false, maxLen: 20),
        Col("DESCART", "nvarchar", SqlTypeCategory.Text, maxLen: 100),
        Col("PRCVENTA", "decimal", SqlTypeCategory.Decimal, precision: 12, scale: 2),
    }, "CODART");

    [Fact]
    public void Detects_field_code_row_when_labels_are_above()
    {
        var raw = TwoHeaderRowSheet();
        var interpreted = new SheetInterpreter().Interpret(raw, ArticuloTable());

        Assert.Equal(2, interpreted.HeaderRowNumber);
        Assert.Equal(new[] { "CODART", "DESCART", "PRCVENTA" }, interpreted.Sheet.Columns);
        Assert.Equal(3, interpreted.MatchedColumns);
        Assert.Single(interpreted.Sheet.Rows);
        Assert.Equal("A100", interpreted.Sheet.Rows[0].Get("CODART").AsDisplayString());
    }

    [Fact]
    public void Forced_header_row_overrides_detection()
    {
        var raw = TwoHeaderRowSheet();
        var interpreted = new SheetInterpreter().Interpret(raw, ArticuloTable(), forcedHeaderRow1Based: 1);

        Assert.Equal(1, interpreted.HeaderRowNumber);
        Assert.Contains("Cód. artículo", interpreted.Sheet.Columns);
    }
}
