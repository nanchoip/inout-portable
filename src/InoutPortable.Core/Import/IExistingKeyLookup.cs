using InoutPortable.Core.Models;

namespace InoutPortable.Core.Import;

/// <summary>A candidate primary-key from an Excel row, with its normalized form and CLR key values.</summary>
public sealed record KeyCandidate(string Normalized, IReadOnlyList<object?> Values);

/// <summary>
/// Given the candidate keys found in a sheet, returns the subset (as normalized strings) that
/// already exist in the destination table. Abstracted so the planner can be unit-tested without a database.
/// </summary>
public interface IExistingKeyLookup
{
    Task<IReadOnlySet<string>> GetExistingKeysAsync(
        TableMetadata table,
        IReadOnlyList<string> keyColumns,
        IReadOnlyList<KeyCandidate> candidates,
        CancellationToken ct = default);
}
