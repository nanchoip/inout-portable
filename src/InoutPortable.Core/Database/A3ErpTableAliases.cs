using System.Globalization;
using System.Text;

namespace InoutPortable.Core.Database;

/// <summary>
/// Maps friendly Spanish sheet names (singular/plural, with or without accents) to the real a3ERP
/// table/view names, so a user can name a sheet "Clientes" or "Artículos" instead of the exact
/// a3ERP code. Only the common master entities are covered; unknown names fall through unchanged.
/// </summary>
public static class A3ErpTableAliases
{
    // Keys are normalized (lower-case, accent-stripped). Values are the real a3ERP object names.
    private static readonly Dictionary<string, string> Map = new(StringComparer.Ordinal)
    {
        ["cliente"] = "CLIENTES",
        ["clientes"] = "CLIENTES",
        ["proveedor"] = "PROVEEDO",
        ["proveedores"] = "PROVEEDO",
        ["articulo"] = "ARTICULO",
        ["articulos"] = "ARTICULO",
        ["producto"] = "ARTICULO",
        ["productos"] = "ARTICULO",
        ["cuenta"] = "CUENTAS",
        ["cuentas"] = "CUENTAS",
        ["almacen"] = "ALMACEN",
        ["almacenes"] = "ALMACEN",
        ["familia"] = "FAMILIAS",
        ["familias"] = "FAMILIAS",
        ["banco"] = "BANCOS",
        ["bancos"] = "BANCOS",
        ["amortizacion"] = "AMORTIZA",
        ["amortizaciones"] = "AMORTIZA",
        ["amortiza"] = "AMORTIZA",
        ["apunte"] = "APUNTES",
        ["apuntes"] = "APUNTES",
        ["asiento"] = "APUNTES",
        ["asientos"] = "APUNTES",
        ["diario"] = "APUNTES",
    };

    /// <summary>Returns the real a3ERP object name for a friendly sheet name, or null if there is no alias.</summary>
    public static string? Resolve(string sheetName)
    {
        if (string.IsNullOrWhiteSpace(sheetName)) return null;
        return Map.TryGetValue(Normalize(sheetName), out var real) ? real : null;
    }

    /// <summary>Lower-cases and strips accents/diacritics so "Artículos" == "articulos".</summary>
    public static string Normalize(string text)
    {
        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
