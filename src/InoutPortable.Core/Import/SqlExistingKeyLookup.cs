using InoutPortable.Core.Database;
using InoutPortable.Core.Models;
using Microsoft.Data.SqlClient;

namespace InoutPortable.Core.Import;

/// <summary>
/// SQL Server implementation of <see cref="IExistingKeyLookup"/>. Queries the destination table in
/// batches to discover which candidate primary keys already exist, so rows can be classified as
/// insert vs update. Can run on its own connection (preview) or on a caller-supplied
/// connection/transaction (execution).
/// </summary>
public sealed class SqlExistingKeyLookup : IExistingKeyLookup
{
    private readonly ConnectionSettings? _settings;
    private readonly SqlConnection? _connection;
    private readonly SqlTransaction? _transaction;

    public SqlExistingKeyLookup(ConnectionSettings settings) => _settings = settings;

    public SqlExistingKeyLookup(SqlConnection connection, SqlTransaction? transaction = null)
    {
        _connection = connection;
        _transaction = transaction;
    }

    public async Task<IReadOnlySet<string>> GetExistingKeysAsync(
        TableMetadata table,
        IReadOnlyList<string> keyColumns,
        IReadOnlyList<KeyCandidate> candidates,
        CancellationToken ct = default)
    {
        var result = new HashSet<string>();
        if (candidates.Count == 0 || keyColumns.Count == 0)
            return result;

        var categories = keyColumns.Select(k => table.GetColumn(k)!.Category).ToList();

        SqlConnection conn;
        bool ownsConnection = false;
        if (_connection is not null)
        {
            conn = _connection;
        }
        else
        {
            conn = new SqlConnection(_settings!.BuildConnectionString());
            await conn.OpenAsync(ct);
            ownsConnection = true;
        }

        try
        {
            if (keyColumns.Count == 1)
                await LookupSingleColumnAsync(conn, table, keyColumns[0], categories[0], candidates, result, ct);
            else
                await LookupCompositeAsync(conn, table, keyColumns, categories, candidates, result, ct);
        }
        finally
        {
            if (ownsConnection)
                await conn.DisposeAsync();
        }

        return result;
    }

    private async Task LookupSingleColumnAsync(SqlConnection conn, TableMetadata table, string keyColumn,
        SqlTypeCategory category, IReadOnlyList<KeyCandidate> candidates, HashSet<string> result, CancellationToken ct)
    {
        const int chunkSize = 900;
        var col = Bracket(keyColumn);

        for (int start = 0; start < candidates.Count; start += chunkSize)
        {
            var chunk = candidates.Skip(start).Take(chunkSize).ToList();
            var names = chunk.Select((_, i) => "@p" + i).ToList();

            await using var cmd = CreateCommand(conn);
            cmd.CommandText = $"SELECT {col} FROM {table.FullName} WHERE {col} IN ({string.Join(",", names)})";
            for (int i = 0; i < chunk.Count; i++)
                cmd.Parameters.AddWithValue(names[i], chunk[i].Values[0] ?? DBNull.Value);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                result.Add(KeyNormalizer.NormalizeValue(category, r.IsDBNull(0) ? null : r.GetValue(0)));
        }
    }

    private async Task LookupCompositeAsync(SqlConnection conn, TableMetadata table, IReadOnlyList<string> keyColumns,
        IReadOnlyList<SqlTypeCategory> categories, IReadOnlyList<KeyCandidate> candidates, HashSet<string> result, CancellationToken ct)
    {
        // Keep the total parameter count under SQL Server's 2100 limit.
        int chunkSize = Math.Max(1, 2000 / keyColumns.Count);
        var selectCols = string.Join(",", keyColumns.Select(Bracket));

        for (int start = 0; start < candidates.Count; start += chunkSize)
        {
            var chunk = candidates.Skip(start).Take(chunkSize).ToList();

            await using var cmd = CreateCommand(conn);
            var orGroups = new List<string>();
            for (int rowIdx = 0; rowIdx < chunk.Count; rowIdx++)
            {
                var conds = new List<string>();
                for (int c = 0; c < keyColumns.Count; c++)
                {
                    string pName = $"@p{rowIdx}_{c}";
                    conds.Add($"{Bracket(keyColumns[c])} = {pName}");
                    cmd.Parameters.AddWithValue(pName, chunk[rowIdx].Values[c] ?? DBNull.Value);
                }
                orGroups.Add("(" + string.Join(" AND ", conds) + ")");
            }

            cmd.CommandText = $"SELECT {selectCols} FROM {table.FullName} WHERE {string.Join(" OR ", orGroups)}";

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var values = new object?[keyColumns.Count];
                for (int c = 0; c < keyColumns.Count; c++)
                    values[c] = r.IsDBNull(c) ? null : r.GetValue(c);
                result.Add(KeyNormalizer.NormalizeTuple(categories, values));
            }
        }
    }

    private SqlCommand CreateCommand(SqlConnection conn)
    {
        var cmd = conn.CreateCommand();
        if (_transaction is not null)
            cmd.Transaction = _transaction;
        return cmd;
    }

    private static string Bracket(string identifier) => "[" + identifier.Replace("]", "]]") + "]";
}
