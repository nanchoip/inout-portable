using InoutPortable.Core.Excel;

namespace InoutPortable.Tests;

public class ExcelWorkbookWriterTests
{
    [Fact]
    public void Written_workbook_round_trips_through_the_reader()
    {
        var sheet = new SheetExport
        {
            Name = "CLIENTES",
            Columns = new[] { "CODCLI", "NOMCLI", "ALTA", "SALDO" },
            Rows = new List<object?[]>
            {
                new object?[] { 1, "Ana", new DateTime(2020, 5, 1), 10.5m },
                new object?[] { 2, "Luis", null, 0m },
            },
        };

        var path = Path.Combine(Path.GetTempPath(), $"inout-export-test-{Guid.NewGuid():N}.xlsx");
        try
        {
            var counts = new ExcelWorkbookWriter().Write(path, new[] { sheet });
            Assert.Equal(2, counts["CLIENTES"]);

            var raw = new ExcelWorkbookReader().ReadRaw(path);
            var interpreted = new SheetInterpreter().Interpret(raw.Single(), table: null);

            Assert.Equal("CLIENTES", interpreted.Sheet.TableName);
            Assert.Equal(new[] { "CODCLI", "NOMCLI", "ALTA", "SALDO" }, interpreted.Sheet.Columns);
            Assert.Equal(2, interpreted.Sheet.Rows.Count);

            var r0 = interpreted.Sheet.Rows[0];
            Assert.True(r0.Get("CODCLI").IsNumber);
            Assert.Equal("Ana", r0.Get("NOMCLI").Raw);
            Assert.True(r0.Get("ALTA").IsDateTime);

            // Null cell in row 2 stays empty.
            Assert.True(interpreted.Sheet.Rows[1].Get("ALTA").IsEmpty);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Long_and_invalid_sheet_names_are_sanitized_and_unique()
    {
        var sheets = new[]
        {
            new SheetExport { Name = "Ventas/2024", Columns = new[] { "A" }, Rows = new List<object?[]>() },
            new SheetExport { Name = "Ventas/2024", Columns = new[] { "A" }, Rows = new List<object?[]>() },
        };
        var path = Path.Combine(Path.GetTempPath(), $"inout-export-names-{Guid.NewGuid():N}.xlsx");
        try
        {
            new ExcelWorkbookWriter().Write(path, sheets);
            var raw = new ExcelWorkbookReader().ReadRaw(path);
            Assert.Equal(2, raw.Count);
            Assert.DoesNotContain(raw, s => s.Name.Contains('/')); // invalid char replaced
            Assert.Equal(raw.Count, raw.Select(s => s.Name).Distinct().Count()); // unique
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
