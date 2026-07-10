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

    public async Task<CompanyListResult> ListCompaniesAsync(CancellationToken ct = default)
    {
        try
        {
            string? systemDb = await FindSystemDatabaseAsync(ct);
            if (systemDb is null)
                return new CompanyListResult(false, null, Array.Empty<A3ErpCompany>(),
                    "No se encontró la base de datos de sistema de a3ERP (…$SISTEMA con tabla EMPRESAS) en este servidor.");

            var companies = await ReadCompaniesAsync(systemDb, ct);
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

    private async Task<IReadOnlyList<A3ErpCompany>> ReadCompaniesAsync(string systemDb, CancellationToken ct)
    {
        var settings = _settings.Clone();
        settings.Database = systemDb;

        await using var conn = new SqlConnection(settings.BuildConnectionString());
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT DESCRIPCION, DATABASENAME, SERVERNAME, IMAGENEMPRESA
FROM dbo.EMPRESAS
WHERE DATABASENAME IS NOT NULL AND DATABASENAME <> ''
ORDER BY DESCRIPCION;";

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
