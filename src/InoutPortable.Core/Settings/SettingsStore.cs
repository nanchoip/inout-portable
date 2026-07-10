using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InoutPortable.Core.Database;
using InoutPortable.Core.Infrastructure;

namespace InoutPortable.Core.Settings;

/// <summary>
/// Persists <see cref="ConnectionSettings"/> to a JSON file next to the app.
/// The password is encrypted at rest with Windows DPAPI (per current user).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SettingsStore
{
    private readonly string _path;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SettingsStore(string? path = null)
    {
        _path = path ?? AppPaths.SettingsFile;
    }

    public bool Exists => File.Exists(_path);

    public ConnectionSettings Load()
    {
        if (!File.Exists(_path))
            return new ConnectionSettings();

        var dto = JsonSerializer.Deserialize<PersistedSettings>(File.ReadAllText(_path))
                  ?? new PersistedSettings();

        return new ConnectionSettings
        {
            Host = dto.Host,
            Instance = dto.Instance,
            Port = dto.Port,
            Database = dto.Database,
            IntegratedSecurity = dto.IntegratedSecurity,
            Username = dto.Username,
            Password = Unprotect(dto.EncryptedPassword),
            Encrypt = dto.Encrypt,
            TrustServerCertificate = dto.TrustServerCertificate,
            ConnectTimeoutSeconds = dto.ConnectTimeoutSeconds <= 0 ? 15 : dto.ConnectTimeoutSeconds,
        };
    }

    public void Save(ConnectionSettings settings)
    {
        var dto = new PersistedSettings
        {
            Host = settings.Host,
            Instance = settings.Instance,
            Port = settings.Port,
            Database = settings.Database,
            IntegratedSecurity = settings.IntegratedSecurity,
            Username = settings.Username,
            EncryptedPassword = Protect(settings.Password),
            Encrypt = settings.Encrypt,
            TrustServerCertificate = settings.TrustServerCertificate,
            ConnectTimeoutSeconds = settings.ConnectTimeoutSeconds,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(dto, JsonOptions));
    }

    [SupportedOSPlatform("windows")]
    private static string Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain))
            return "";
        try
        {
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(bytes);
        }
        catch
        {
            // DPAPI unavailable (non-Windows / restricted). Fail closed: do not persist plaintext.
            return "";
        }
    }

    [SupportedOSPlatform("windows")]
    private static string Unprotect(string? encrypted)
    {
        if (string.IsNullOrEmpty(encrypted))
            return "";
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(encrypted), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            // Wrong user/machine or corrupted blob -> user must re-enter the password.
            return "";
        }
    }

    private sealed class PersistedSettings
    {
        public string Host { get; set; } = "";
        public string? Instance { get; set; }
        public int? Port { get; set; }
        public string Database { get; set; } = "";
        public bool IntegratedSecurity { get; set; }
        public string Username { get; set; } = "";
        public string EncryptedPassword { get; set; } = "";
        public bool Encrypt { get; set; } = true;
        public bool TrustServerCertificate { get; set; } = true;
        public int ConnectTimeoutSeconds { get; set; } = 15;
    }
}
