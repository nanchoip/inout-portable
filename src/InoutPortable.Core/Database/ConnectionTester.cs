using System.Diagnostics;
using Microsoft.Data.SqlClient;

namespace InoutPortable.Core.Database;

public sealed record ConnectionTestResult(bool Success, string Message, string? ServerVersion = null, long ElapsedMs = 0);

/// <summary>Opens a short-lived connection to verify the configured SQL Server is reachable and the credentials work.</summary>
public sealed class ConnectionTester
{
    public async Task<ConnectionTestResult> TestAsync(ConnectionSettings settings, CancellationToken ct = default)
    {
        var validation = settings.Validate();
        if (validation.Count > 0)
            return new ConnectionTestResult(false, string.Join(Environment.NewLine, validation));

        var sw = Stopwatch.StartNew();
        try
        {
            await using var conn = new SqlConnection(settings.BuildConnectionString());
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT @@VERSION, DB_NAME()";
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            string version = "", dbName = "";
            if (await reader.ReadAsync(ct))
            {
                version = reader.GetString(0);
                dbName = reader.IsDBNull(1) ? "" : reader.GetString(1);
            }

            sw.Stop();
            var shortVersion = version.Split('\n').FirstOrDefault()?.Trim() ?? version;
            return new ConnectionTestResult(
                true,
                $"Conexión correcta a la base de datos '{dbName}' ({sw.ElapsedMilliseconds} ms).",
                shortVersion,
                sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            return new ConnectionTestResult(false, "La prueba de conexión se canceló.");
        }
        catch (SqlException ex)
        {
            return new ConnectionTestResult(false, DescribeSqlError(ex));
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult(false, $"No se pudo conectar: {ex.Message}");
        }
    }

    private static string DescribeSqlError(SqlException ex)
    {
        // Map the most common failure numbers to actionable Spanish guidance.
        return ex.Number switch
        {
            18456 => "Error de autenticación: usuario o contraseña incorrectos.",
            4060 => "No se puede abrir la base de datos indicada. Verifique el nombre.",
            53 or -1 or 2 => "No se encuentra el servidor. Verifique el host/puerto y que SQL Server acepte conexiones TCP.",
            10060 or 10061 => "Conexión rechazada o expirada. Revise el firewall y que el puerto esté abierto.",
            _ => $"Error de SQL Server ({ex.Number}): {ex.Message}",
        };
    }
}
