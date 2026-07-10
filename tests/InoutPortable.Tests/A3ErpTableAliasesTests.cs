using InoutPortable.Core.Database;

namespace InoutPortable.Tests;

public class A3ErpTableAliasesTests
{
    [Theory]
    [InlineData("Clientes", "CLIENTES")]
    [InlineData("cliente", "CLIENTES")]
    [InlineData("Artículos", "ARTICULO")]
    [InlineData("articulos", "ARTICULO")]
    [InlineData("ARTÍCULO", "ARTICULO")]
    [InlineData("Productos", "ARTICULO")]
    [InlineData("Cuentas", "CUENTAS")]
    [InlineData("Almacenes", "ALMACEN")]
    [InlineData("Familias", "FAMILIAS")]
    [InlineData("Bancos", "BANCOS")]
    [InlineData("Amortizaciones", "AMORTIZA")]
    [InlineData("Asientos", "APUNTES")]
    [InlineData("Diario", "APUNTES")]
    public void Resolves_friendly_names_to_a3erp_tables(string input, string expected)
    {
        Assert.Equal(expected, A3ErpTableAliases.Resolve(input));
    }

    [Theory]
    [InlineData("TablaDesconocida")]
    [InlineData("CABEFACV")] // a real a3ERP name with no friendly alias
    [InlineData("")]
    public void Returns_null_for_unknown_names(string input)
    {
        Assert.Null(A3ErpTableAliases.Resolve(input));
    }

    [Fact]
    public void Normalize_strips_accents_and_case()
    {
        Assert.Equal("articulos", A3ErpTableAliases.Normalize("  Artículos "));
        Assert.Equal("almacen", A3ErpTableAliases.Normalize("Almacén"));
    }
}
