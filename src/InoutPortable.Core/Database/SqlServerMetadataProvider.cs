using InoutPortable.Core.Models;
using Microsoft.Data.SqlClient;

namespace InoutPortable.Core.Database;

/// <summary>Outcome of resolving a worksheet name to a real table in the database.</summary>
public sealed record TableLookup(TableLookupStatus Status, TableMetadata? Table, string? Message)
{
    public static TableLookup Found(TableMetadata t) => new(TableLookupStatus.Found, t, null);
    public static TableLookup NotFound(string msg) => new(TableLookupStatus.NotFound, null, msg);
    public static TableLookup Ambiguous(string msg) => new(TableLookupStatus.Ambiguous, null, msg);
}

public enum TableLookupStatus { Found, NotFound, Ambiguous }

/// <summary>
/// Reads schema metadata from SQL Server: resolves sheet names to tables, and loads columns
/// (types, lengths, nullability, identity/computed) and the primary key.
/// </summary>
public sealed class SqlServerMetadataProvider
{
    private readonly ConnectionSettings _settings;

    public SqlServerMetadataProvider(ConnectionSettings settings) => _settings = settings;

    private SqlConnection CreateConnection() => new(_settings.BuildConnectionString());

    /// <summary>Resolves a worksheet name (optionally <c>schema.table</c>) to a single table's metadata.</summary>
    public async Task<TableLookup> LookupTableAsync(string sheetName, CancellationToken ct = default)
    {
        string? schema = null;
        string name = sheetName.Trim();

        // Accept "schema.table" and "[schema].[table]".
        var unbracketed = name.Replace("[", "").Replace("]", "");
        var dot = unbracketed.IndexOf('.');
        if (dot > 0)
        {
            schema = unbracketed[..dot].Trim();
            name = unbracketed[(dot + 1)..].Trim();
        }

        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        var matches = new List<(string Schema, string Name)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = schema is null
                ? "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE' AND TABLE_NAME=@n"
                : "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE' AND TABLE_NAME=@n AND TABLE_SCHEMA=@s";
            cmd.Parameters.AddWithValue("@n", name);
            if (schema is not null) cmd.Parameters.AddWithValue("@s", schema);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                matches.Add((r.GetString(0), r.GetString(1)));
        }

        if (matches.Count == 0)
            return TableLookup.NotFound($"No existe la tabla '{sheetName}' en la base de datos.");
        if (matches.Count > 1)
            return TableLookup.Ambiguous(
                $"El nombre '{name}' existe en varios esquemas ({string.Join(", ", matches.Select(m => m.Schema))}). " +
                $"Indique el esquema en el nombre de la hoja (p. ej. 'dbo.{name}').");

        var (foundSchema, foundName) = matches[0];
        var table = await LoadTableAsync(conn, foundSchema, foundName, ct);
        return TableLookup.Found(table);
    }

    private static async Task<TableMetadata> LoadTableAsync(SqlConnection conn, string schema, string name, CancellationToken ct)
    {
        var columns = new List<ColumnMetadata>();

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT
    c.COLUMN_NAME,
    c.DATA_TYPE,
    c.CHARACTER_MAXIMUM_LENGTH,
    c.NUMERIC_PRECISION,
    c.NUMERIC_SCALE,
    c.IS_NULLABLE,
    c.ORDINAL_POSITION,
    COLUMNPROPERTY(OBJECT_ID(QUOTENAME(c.TABLE_SCHEMA) + '.' + QUOTENAME(c.TABLE_NAME)), c.COLUMN_NAME, 'IsIdentity') AS IsIdentity,
    COLUMNPROPERTY(OBJECT_ID(QUOTENAME(c.TABLE_SCHEMA) + '.' + QUOTENAME(c.TABLE_NAME)), c.COLUMN_NAME, 'IsComputed') AS IsComputed
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_SCHEMA = @s AND c.TABLE_NAME = @n
ORDER BY c.ORDINAL_POSITION;";
            cmd.Parameters.AddWithValue("@s", schema);
            cmd.Parameters.AddWithValue("@n", name);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                string dataType = r.GetString(1);
                columns.Add(new ColumnMetadata
                {
                    Name = r.GetString(0),
                    SqlType = dataType,
                    Category = Categorize(dataType),
                    MaxLength = r.IsDBNull(2) ? null : r.GetInt32(2),
                    Precision = r.IsDBNull(3) ? null : System.Convert.ToByte(r.GetValue(3)),
                    Scale = r.IsDBNull(4) ? null : System.Convert.ToInt32(r.GetValue(4)),
                    IsNullable = r.GetString(5).Equals("YES", StringComparison.OrdinalIgnoreCase),
                    OrdinalPosition = r.GetInt32(6),
                    IsIdentity = !r.IsDBNull(7) && r.GetInt32(7) == 1,
                    IsComputed = !r.IsDBNull(8) && r.GetInt32(8) == 1,
                });
            }
        }

        var pk = new List<string>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT kcu.COLUMN_NAME
FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
    ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
   AND tc.TABLE_SCHEMA = kcu.TABLE_SCHEMA
   AND tc.TABLE_NAME = kcu.TABLE_NAME
WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY' AND tc.TABLE_SCHEMA = @s AND tc.TABLE_NAME = @n
ORDER BY kcu.ORDINAL_POSITION;";
            cmd.Parameters.AddWithValue("@s", schema);
            cmd.Parameters.AddWithValue("@n", name);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                pk.Add(r.GetString(0));
        }

        return new TableMetadata
        {
            Schema = schema,
            Name = name,
            Columns = columns,
            PrimaryKey = pk,
        };
    }

    public static SqlTypeCategory Categorize(string sqlType) => sqlType.ToLowerInvariant() switch
    {
        "char" or "varchar" or "nchar" or "nvarchar" or "text" or "ntext" or "xml" or "sysname" => SqlTypeCategory.Text,
        "tinyint" or "smallint" or "int" or "bigint" => SqlTypeCategory.Integer,
        "decimal" or "numeric" or "money" or "smallmoney" => SqlTypeCategory.Decimal,
        "float" or "real" => SqlTypeCategory.Float,
        "bit" => SqlTypeCategory.Bit,
        "date" or "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset" => SqlTypeCategory.DateTime,
        "time" => SqlTypeCategory.Time,
        "uniqueidentifier" => SqlTypeCategory.Guid,
        "binary" or "varbinary" or "image" or "timestamp" or "rowversion" => SqlTypeCategory.Binary,
        _ => SqlTypeCategory.Other,
    };
}
