namespace InoutPortable.Core.Database;

/// <summary>Connection info read from a3ERP's <c>Sistema.ini</c> (the system server + system database).</summary>
public sealed record A3ErpConnectionInfo(string? Server, string? SystemDatabase)
{
    public bool HasAny => !string.IsNullOrWhiteSpace(Server) || !string.IsNullOrWhiteSpace(SystemDatabase);
}

/// <summary>
/// Reads a3ERP's <c>Sistema.ini</c> the same way the native a3ERPInOut does at startup: it holds the
/// SQL Server (<c>Servidor=</c>) and the system database (<c>BaseDatos=</c>). Only present when the
/// a3ERP client is installed on this machine; otherwise the user uses "Buscar instancias" instead.
/// </summary>
public static class SistemaIniReader
{
    private static readonly string[] ServerKeys = { "servidor", "server", "datasource", "data source" };
    private static readonly string[] DatabaseKeys = { "basedatos", "basededatos", "database", "catalogo", "catálogo", "initialcatalog", "initial catalog" };

    /// <summary>Parses the INI text and extracts the server and system-database values.</summary>
    public static A3ErpConnectionInfo Parse(string iniContent)
    {
        string? server = null, database = null;
        foreach (var raw in iniContent.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#') || line.StartsWith('['))
                continue;

            int eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim().ToLowerInvariant();
            var value = line[(eq + 1)..].Trim();
            if (value.Length == 0) continue;

            if (server is null && ServerKeys.Contains(key)) server = value;
            else if (database is null && DatabaseKeys.Contains(key)) database = value;
        }
        return new A3ErpConnectionInfo(server, database);
    }

    /// <summary>Tries to locate Sistema.ini inside a local a3ERP installation. Returns null if not found.</summary>
    public static string? Locate()
    {
        foreach (var dir in CandidateDirectories())
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                var hit = Directory.EnumerateFiles(dir, "Sistema.ini", SearchOption.AllDirectories).FirstOrDefault();
                if (hit is not null) return hit;
            }
            catch { /* access denied / long paths -> skip this root */ }
        }
        return null;
    }

    public static A3ErpConnectionInfo? LocateAndRead()
    {
        var path = Locate();
        return path is null ? null : Parse(File.ReadAllText(path));
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            @"C:\",
        };

        foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r) && Directory.Exists(r)))
        {
            // Only look inside a3ERP-named folders to keep the scan fast.
            IEnumerable<string> a3Dirs;
            try { a3Dirs = Directory.EnumerateDirectories(root, "a3*"); }
            catch { continue; }
            foreach (var d in a3Dirs)
                yield return d;
        }
    }
}
