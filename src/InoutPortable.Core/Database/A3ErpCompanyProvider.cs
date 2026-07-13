using Microsoft.Data.SqlClient;

namespace InoutPortable.Core.Database;

/// <summary>A company registered in a3ERP's system database (table EMPRESAS).</summary>
public sealed record A3ErpCompany(string Description, string DatabaseName, string? ServerName, byte[]? Logo)
{
    public bool HasLogo => Logo is { Length: > 0 };
}

public sealed record CompanyListResult(bool Success, string? SystemDatabase, IReadOnlyList<A3ErpCompany> Companies, string? Error);

/// <summary>
/// Reads the list of a3ERP companies from the system database (e.g. <c>A3ERP$SISTEMA</c>), exactly like
/// a3ERP's native company selector: each row of <c>EMPRESAS</c> maps a company name to its real data
/// database (<c>DATABASENAME</c>) and server (<c>SERVERNAME</c>).
/// </summary>
public sealed class A3ErpCompanyProvider
{
    private readonly ConnectionSettings _settings;

    public A3ErpCompanyProvider(ConnectionSettings settings) => _settings = settings;

    public async Task<CompanyListResult> ListCompaniesAsync(
        string? a3ErpUser = null, string? systemDbOverride = null, CancellationToken ct = default)
    {
        try
        {
            string? systemDb = !string.IsNullOrWhiteSpace(systemDbOverride)
                ? systemDbOverride
                : await FindSystemDatabaseAsync(ct);
            if (systemDb is null)
                return new CompanyListResult(false, null, Array.Empty<A3ErpCompany>(),
                    "No se encontró la base de datos de sistema de a3ERP (…$SISTEMA con tabla EMPRESAS) en este servidor.");

            var companies = await ReadCompaniesAsync(systemDb, a3ErpUser, ct);
            return new CompanyListResult(true, systemDb, companies, null);
        }
        catch (SqlException ex)
        {
            return new CompanyListResult(false, null, Array.Empty<A3ErpCompany>(), $"Error de SQL Server: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new CompanyListResult(false, null, Array.Empty<A3ErpCompany>(), ex.Message);
        }
    }

    /// <summary>Finds a database named like <c>%$SISTEMA</c> that contains <c>dbo.EMPRESAS</c> with a DATABASENAME column.</summary>
    private async Task<string?> FindSystemDatabaseAsync(CancellationToken ct)
    {
        // Connect without a specific catalog so this works regardless of the current Database setting.
        await using var conn = new SqlConnection(_settings.BuildConnectionString(includeDatabase: false));
        await conn.OpenAsync(ct);

        var candidates = new List<string>();
        await using (var cmd = conn.CreateCommand())
        {
            // Prefer the standard A3ERP$SISTEMA name first.
            cmd.CommandText = @"
SELECT name FROM sys.databases
WHERE name LIKE '%$SISTEMA' AND state = 0
ORDER BY CASE WHEN name = 'A3ERP$SISTEMA' THEN 0 ELSE 1 END, name;";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                candidates.Add(r.GetString(0));
        }

        foreach (var db in candidates)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COL_LENGTH(QUOTENAME(@db) + '.dbo.EMPRESAS', 'DATABASENAME')";
            cmd.Parameters.AddWithValue("@db", db);
            var result = await cmd.ExecuteScalarAsync(ct);
            if (result is not null && result != DBNull.Value)
                return db;
        }

        return null;
    }

    private async Task<IReadOnlyList<A3ErpCompany>> ReadCompaniesAsync(string systemDb, string? a3ErpUser, CancellationToken ct)
    {
        var settings = _settings.Clone();
        settings.Database = systemDb;

        await using var conn = new SqlConnection(settings.BuildConnectionString());
        await conn.OpenAsync(ct);

        // Faithful permission filter (like a3ERP's native selector): if an a3ERP user is given and that
        // user has explicit rows in __EMPRESASUSUARIO, restrict to those companies; otherwise show all.
        bool applyUserFilter = false;
        if (!string.IsNullOrWhiteSpace(a3ErpUser))
        {
            // Check table existence and the user's rows in TWO steps: SQL Server resolves object names
            // at compile time, so a single batch that references __EMPRESASUSUARIO would fail to compile
            // (Msg 208) when the table is absent, even behind an OBJECT_ID guard.
            await using var existsCmd = conn.CreateCommand();
            existsCmd.CommandText = "SELECT OBJECT_ID('dbo.__EMPRESASUSUARIO')";
            bool tableExists = (await existsCmd.ExecuteScalarAsync(ct)) is not (null or DBNull);

            if (tableExists)
            {
                await using var countCmd = conn.CreateCommand();
                countCmd.CommandText = "SELECT COUNT(*) FROM dbo.__EMPRESASUSUARIO WHERE USUARIO = @u";
                countCmd.Parameters.AddWithValue("@u", a3ErpUser);
                applyUserFilter = (int)(await countCmd.ExecuteScalarAsync(ct) ?? 0) > 0;
            }
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT e.DESCRIPCION, e.DATABASENAME, e.SERVERNAME, e.IMAGENEMPRESA
FROM dbo.EMPRESAS e
WHERE e.DATABASENAME IS NOT NULL AND e.DATABASENAME <> ''"
            + (applyUserFilter
                ? " AND EXISTS (SELECT 1 FROM dbo.__EMPRESASUSUARIO eu WHERE eu.USUARIO = @u AND eu.IDEMP = e.IDEMP)"
                : "")
            + " ORDER BY e.DESCRIPCION;";
        if (applyUserFilter)
            cmd.Parameters.AddWithValue("@u", a3ErpUser!);

        var companies = new List<A3ErpCompany>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            string desc = r.IsDBNull(0) ? "" : r.GetString(0);
            string dbName = r.IsDBNull(1) ? "" : r.GetString(1);
            string? server = r.IsDBNull(2) ? null : r.GetString(2);
            byte[]? logo = r.IsDBNull(3) ? null : (byte[])r.GetValue(3);
            if (!string.IsNullOrWhiteSpace(dbName))
                companies.Add(new A3ErpCompany(desc, dbName, server, logo));
        }

        return companies;
    }
}
