namespace InoutPortable.Core.Infrastructure;

/// <summary>
/// Resolves where the portable app keeps its writable data (settings + import logs).
/// Prefers a <c>data</c> folder next to the executable so everything travels together;
/// falls back to %LOCALAPPDATA%\InoutPortable when the exe folder is read-only
/// (e.g. launched from Program Files or a read-only medium).
/// </summary>
public static class AppPaths
{
    private static readonly Lazy<string> _dataDir = new(ResolveDataDirectory);

    public static string DataDirectory => _dataDir.Value;

    public static string SettingsFile => Path.Combine(DataDirectory, "connection.json");

    public static string ImportLogFile => Path.Combine(DataDirectory, "import-history.jsonl");

    /// <summary>Directory where the running executable lives (works for single-file publish too).</summary>
    public static string ExecutableDirectory
    {
        get
        {
            var exe = Environment.ProcessPath;
            return string.IsNullOrEmpty(exe)
                ? AppContext.BaseDirectory
                : Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory;
        }
    }

    private static string ResolveDataDirectory()
    {
        var candidate = Path.Combine(ExecutableDirectory, "data");
        if (TryEnsureWritable(candidate))
            return candidate;

        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "InoutPortable");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    private static bool TryEnsureWritable(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".write-probe");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
