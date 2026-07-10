using Microsoft.Data.SqlClient;

namespace InoutPortable.Core.Database;

/// <summary>
/// User-configurable connection parameters for the target SQL Server.
/// Password is held in memory as plain text and only persisted encrypted (see SettingsStore).
/// </summary>
public sealed class ConnectionSettings
{
    public string Host { get; set; } = "";

    /// <summary>Optional named instance (e.g. <c>SQLEXPRESS</c>). Mutually exclusive with an explicit port.</summary>
    public string? Instance { get; set; }

    /// <summary>Optional TCP port. Ignored when <see cref="Instance"/> is set.</summary>
    public int? Port { get; set; }

    public string Database { get; set; } = "";

    /// <summary>When true, use Windows integrated auth and ignore username/password.</summary>
    public bool IntegratedSecurity { get; set; }

    public string Username { get; set; } = "";

    public string Password { get; set; } = "";

    /// <summary>Encrypt the connection (TLS). Defaults to true (SqlClient 4+ default).</summary>
    public bool Encrypt { get; set; } = true;

    /// <summary>Trust the server certificate without validation. Common for internal servers with self-signed certs.</summary>
    public bool TrustServerCertificate { get; set; } = true;

    public int ConnectTimeoutSeconds { get; set; } = 15;

    /// <summary>Builds the data source token: <c>host\instance</c>, <c>host,port</c>, or <c>host</c>.</summary>
    public string BuildDataSource()
    {
        if (!string.IsNullOrWhiteSpace(Instance))
            return $"{Host}\\{Instance}";
        if (Port is > 0)
            return $"{Host},{Port}";
        return Host;
    }

    public string BuildConnectionString(bool includeDatabase = true)
    {
        var b = new SqlConnectionStringBuilder
        {
            DataSource = BuildDataSource(),
            Encrypt = Encrypt,
            TrustServerCertificate = TrustServerCertificate,
            ConnectTimeout = ConnectTimeoutSeconds,
            ApplicationName = "InoutPortable",
            Pooling = true,
        };

        if (includeDatabase && !string.IsNullOrWhiteSpace(Database))
            b.InitialCatalog = Database;

        if (IntegratedSecurity)
        {
            b.IntegratedSecurity = true;
        }
        else
        {
            b.UserID = Username;
            b.Password = Password;
        }

        return b.ConnectionString;
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Host))
            errors.Add("El servidor (host) es obligatorio.");
        if (string.IsNullOrWhiteSpace(Database))
            errors.Add("El nombre de la base de datos es obligatorio.");
        if (!IntegratedSecurity && string.IsNullOrWhiteSpace(Username))
            errors.Add("El usuario es obligatorio (o active la autenticación de Windows).");
        if (Port is < 1 or > 65535 && Port is not null)
            errors.Add("El puerto debe estar entre 1 y 65535.");
        return errors;
    }

    public ConnectionSettings Clone() => (ConnectionSettings)MemberwiseClone();
}
