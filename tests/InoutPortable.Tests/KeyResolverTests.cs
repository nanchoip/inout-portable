using InoutPortable.Core.Import;
using InoutPortable.Core.Models;
using static InoutPortable.Tests.TestHelpers;

namespace InoutPortable.Tests;

public class KeyResolverTests
{
    private static TableMetadata TableWith(IReadOnlyList<string> pk, params IReadOnlyList<string>[] uniqueKeys) => new()
    {
        Name = "T",
        Columns = new[]
        {
            Col("IDCUENTA", "int", SqlTypeCategory.Integer),
            Col("PLACON", "varchar", SqlTypeCategory.Text),
            Col("CUENTA", "varchar", SqlTypeCategory.Text),
            Col("DESCCUE", "varchar", SqlTypeCategory.Text),
        },
        PrimaryKey = pk,
        UniqueKeys = uniqueKeys,
    };

    [Fact]
    public void Manual_override_wins()
    {
        var table = TableWith(new[] { "IDCUENTA" }, new[] { "PLACON", "CUENTA" });
        var key = KeyResolver.Resolve(table, new[] { "CUENTA", "DESCCUE" }, new[] { "CUENTA" });
        Assert.Equal(new[] { "CUENTA" }, key);
    }

    [Fact]
    public void Uses_primary_key_when_present_in_sheet()
    {
        var table = TableWith(new[] { "IDCUENTA" }, new[] { "PLACON", "CUENTA" });
        var key = KeyResolver.Resolve(table, new[] { "IDCUENTA", "DESCCUE" }, null);
        Assert.Equal(new[] { "IDCUENTA" }, key);
    }

    [Fact]
    public void Falls_back_to_unique_key_when_pk_absent_from_sheet()
    {
        // Surrogate PK (IDCUENTA) not in the Excel, but the natural unique key [PLACON, CUENTA] is.
        var table = TableWith(new[] { "IDCUENTA" }, new[] { "PLACON", "CUENTA" });
        var key = KeyResolver.Resolve(table, new[] { "PLACON", "CUENTA", "DESCCUE" }, null);
        Assert.Equal(new[] { "PLACON", "CUENTA" }, key);
    }

    [Fact]
    public void Prefers_the_smallest_present_unique_key()
    {
        var table = TableWith(new[] { "IDCUENTA" },
            new[] { "PLACON", "CUENTA" },
            new[] { "CUENTA" });
        var key = KeyResolver.Resolve(table, new[] { "PLACON", "CUENTA" }, null);
        Assert.Equal(new[] { "CUENTA" }, key);
    }

    [Fact]
    public void Returns_pk_when_nothing_matches_so_planner_blocks()
    {
        var table = TableWith(new[] { "IDCUENTA" }, new[] { "PLACON", "CUENTA" });
        var key = KeyResolver.Resolve(table, new[] { "DESCCUE" }, null);
        Assert.Equal(new[] { "IDCUENTA" }, key); // not present -> planner will block & ask for a key
    }
}
