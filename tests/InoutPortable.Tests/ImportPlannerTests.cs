using InoutPortable.Core.Import;
using InoutPortable.Core.Models;
using static InoutPortable.Tests.TestHelpers;

namespace InoutPortable.Tests;

public class ImportPlannerTests
{
    private static TableMetadata ClientesTable() => Table("Clientes", new[]
    {
        Col("Id", "int", SqlTypeCategory.Integer, nullable: false),
        Col("Nombre", "nvarchar", SqlTypeCategory.Text, nullable: false, maxLen: 50),
        Col("Saldo", "decimal", SqlTypeCategory.Decimal, nullable: true, precision: 10, scale: 2),
    }, "Id");

    private static SheetData ClientesSheet(params RowData[] rows) =>
        Sheet("Clientes", new[] { "Id", "Nombre", "Saldo" }, rows);

    [Fact]
    public async Task Existing_key_becomes_update_and_new_key_becomes_insert()
    {
        var table = ClientesTable();
        var sheet = ClientesSheet(
            Row(2, ("Id", 1d), ("Nombre", "Ana"), ("Saldo", 10.5d)),
            Row(3, ("Id", 2d), ("Nombre", "Luis"), ("Saldo", 0d)));

        // Id == 1 already exists in the DB.
        var lookup = new FakeExistingKeyLookup(c => Convert.ToInt64(c.Values[0]) == 1);

        var plan = await new ImportPlanner().BuildPlanAsync(sheet, table, table.PrimaryKey, lookup);

        Assert.False(plan.IsBlocked);
        Assert.Equal(1, plan.UpdateCount);
        Assert.Equal(1, plan.InsertCount);
        Assert.Equal(0, plan.ErrorCount);
    }

    [Fact]
    public async Task Unknown_column_blocks_the_sheet()
    {
        var table = ClientesTable();
        var sheet = Sheet("Clientes", new[] { "Id", "Nombre", "NoExiste" },
            Row(2, ("Id", 1d), ("Nombre", "Ana"), ("NoExiste", "x")));

        var plan = await new ImportPlanner().BuildPlanAsync(sheet, table, table.PrimaryKey, FakeExistingKeyLookup.None);

        Assert.True(plan.IsBlocked);
        Assert.Contains(plan.Issues, i => i.Column == "NoExiste" && i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public async Task Missing_primary_key_blocks_and_asks_for_key()
    {
        var noPkTable = Table("Cosas", new[]
        {
            Col("Codigo", "nvarchar", SqlTypeCategory.Text, nullable: false, maxLen: 10),
            Col("Valor", "int", SqlTypeCategory.Integer),
        }); // no PK
        var sheet = Sheet("Cosas", new[] { "Codigo", "Valor" }, Row(2, ("Codigo", "A"), ("Valor", 1d)));

        var plan = await new ImportPlanner().BuildPlanAsync(sheet, noPkTable, noPkTable.PrimaryKey, FakeExistingKeyLookup.None);

        Assert.True(plan.IsBlocked);
        Assert.Contains(plan.Issues, i => i.Message.Contains("clave", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Required_column_empty_produces_row_error()
    {
        var table = ClientesTable();
        var sheet = ClientesSheet(
            Row(2, ("Id", 1d), ("Nombre", ""), ("Saldo", 5d))); // Nombre required but empty

        var plan = await new ImportPlanner().BuildPlanAsync(sheet, table, table.PrimaryKey, FakeExistingKeyLookup.None);

        Assert.Equal(1, plan.ErrorCount);
        Assert.Contains(plan.Issues, i => i.Row == 2 && i.Column == "Nombre");
    }

    [Fact]
    public async Task Bad_type_produces_row_error_not_crash()
    {
        var table = ClientesTable();
        var sheet = ClientesSheet(
            Row(2, ("Id", "abc"), ("Nombre", "Ana"), ("Saldo", 5d))); // Id not an integer

        var plan = await new ImportPlanner().BuildPlanAsync(sheet, table, table.PrimaryKey, FakeExistingKeyLookup.None);

        Assert.Equal(1, plan.ErrorCount);
        Assert.Contains(plan.Issues, i => i.Row == 2 && i.Column == "Id");
    }

    [Fact]
    public async Task Duplicate_key_in_sheet_is_flagged()
    {
        var table = ClientesTable();
        var sheet = ClientesSheet(
            Row(2, ("Id", 1d), ("Nombre", "Ana"), ("Saldo", 1d)),
            Row(3, ("Id", 1d), ("Nombre", "Ana2"), ("Saldo", 2d)));

        var plan = await new ImportPlanner().BuildPlanAsync(sheet, table, table.PrimaryKey, FakeExistingKeyLookup.None);

        Assert.Contains(plan.Issues, i => i.Message.Contains("duplicada", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, plan.ErrorCount);
    }
}
