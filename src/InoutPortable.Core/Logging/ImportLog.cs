using System.Text.Json;
using InoutPortable.Core.Infrastructure;
using InoutPortable.Core.Models;

namespace InoutPortable.Core.Logging;

/// <summary>A single persisted import-run record (one JSON object per line in the history file).</summary>
public sealed class ImportLogEntry
{
    public DateTime Timestamp { get; set; }
    public string FileName { get; set; } = "";
    public bool Success { get; set; }
    public bool RolledBack { get; set; }
    public int TotalInserted { get; set; }
    public int TotalUpdated { get; set; }
    public int TotalSkipped { get; set; }
    public string? Message { get; set; }
    public List<TableLogEntry> Tables { get; set; } = new();

    public sealed class TableLogEntry
    {
        public string Sheet { get; set; } = "";
        public string Table { get; set; } = "";
        public int Inserted { get; set; }
        public int Updated { get; set; }
        public int Skipped { get; set; }
        public bool Success { get; set; }
        public string? Error { get; set; }
    }
}

/// <summary>Appends and reads import-run history from a JSONL file next to the app.</summary>
public sealed class ImportLogStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public ImportLogStore(string? path = null) => _path = path ?? AppPaths.ImportLogFile;

    public void Append(ImportResult result)
    {
        var entry = new ImportLogEntry
        {
            Timestamp = result.StartedAt,
            FileName = result.FileName,
            Success = result.Success,
            RolledBack = result.RolledBack,
            TotalInserted = result.TotalInserted,
            TotalUpdated = result.TotalUpdated,
            TotalSkipped = result.TotalSkipped,
            Message = result.Message,
            Tables = result.Tables.Select(t => new ImportLogEntry.TableLogEntry
            {
                Sheet = t.Sheet,
                Table = t.Table,
                Inserted = t.Inserted,
                Updated = t.Updated,
                Skipped = t.SkippedWithErrors,
                Success = t.Success,
                Error = t.ErrorMessage,
            }).ToList(),
        };

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.AppendAllText(_path, JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine);
    }

    /// <summary>Returns history entries, most recent first.</summary>
    public IReadOnlyList<ImportLogEntry> ReadAll()
    {
        if (!File.Exists(_path))
            return Array.Empty<ImportLogEntry>();

        var entries = new List<ImportLogEntry>();
        foreach (var line in File.ReadLines(_path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var e = JsonSerializer.Deserialize<ImportLogEntry>(line);
                if (e is not null) entries.Add(e);
            }
            catch { /* skip corrupted lines */ }
        }

        entries.Reverse();
        return entries;
    }
}
