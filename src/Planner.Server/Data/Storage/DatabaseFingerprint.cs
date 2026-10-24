using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Planner.Server.Data.Storage;

/// <summary>Число строк и контрольная сумма содержимого одной таблицы (docs/specs/accounts.md, X2).</summary>
public sealed record TableFingerprint(string Table, long Rows, string Hash);

/// <summary>
/// «Отпечаток» базы для сверки до/после переноса: каждая таблица (и sqlite_sequence — счётчики Id) — число строк
/// и SHA-256 всех значений. Строки сортируются по всем столбцам: VACUUM может перенумеровать скрытый rowid,
/// поэтому порядок rowid не годится.
/// </summary>
public static class DatabaseFingerprint
{
    /// <param name="only">Только эти таблицы и столбцы (сверка миграции: новые столбцы и таблицы не в счёт); null — всё.</param>
    public static async Task<IReadOnlyList<TableFingerprint>> ComputeAsync(
        string path, IReadOnlyDictionary<string, IReadOnlyList<string>>? only = null, CancellationToken ct = default)
    {
        var schema = await SchemaAsync(path, ct);
        await using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(ct);

        var result = new List<TableFingerprint>();
        foreach (var (table, columns) in only ?? schema)
        {
            if (schema.TryGetValue(table, out var present))
            {
                result.Add(await HashTableAsync(connection, table, columns.Where(present.Contains).ToList(), ct));
            }
        }

        return result;
    }

    /// <summary>Таблицы базы (и sqlite_sequence) и их столбцы по порядку.</summary>
    public static async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> SchemaAsync(string path, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(ct);
        var tables = await ReadNamesAsync(connection,
            "SELECT name FROM sqlite_master WHERE type = 'table' AND (name NOT LIKE 'sqlite_%' OR name = 'sqlite_sequence') ORDER BY name",
            null, ct);
        var schema = new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            schema[table] = await ReadNamesAsync(connection, "SELECT name FROM pragma_table_info(@table) ORDER BY cid", table, ct);
        }

        return schema;
    }

    /// <summary>Таблицы, у которых не совпало число строк или сумма (и таблицы, которых нет с одной из сторон).</summary>
    public static IReadOnlyList<string> Differences(IReadOnlyList<TableFingerprint> before, IReadOnlyList<TableFingerprint> after)
    {
        var afterByTable = after.ToDictionary(t => t.Table, StringComparer.Ordinal);
        var differences = before.Where(b => !afterByTable.TryGetValue(b.Table, out var a) || a != b).Select(b => b.Table).ToList();
        differences.AddRange(after.Select(a => a.Table).Except(before.Select(b => b.Table), StringComparer.Ordinal));
        return differences;
    }

    private static async Task<TableFingerprint> HashTableAsync(
        SqliteConnection connection, string table, IReadOnlyList<string> columns, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        var orderBy = string.Join(", ", Enumerable.Range(1, columns.Count));
        command.CommandText = $"SELECT {string.Join(", ", columns.Select(Quote))} FROM {Quote(table)} ORDER BY {orderBy}";

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long rows = 0;
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows++;
            for (var i = 0; i < reader.FieldCount; i++)
            {
                hash.AppendData(Encoding.UTF8.GetBytes(Format(reader, i)));
                hash.AppendData("\u001f"u8);
            }

            hash.AppendData("\u001e"u8);
        }

        return new TableFingerprint(table, rows, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    /// <summary>Значение с меткой типа: число 1 и строка «1» дают разные суммы.</summary>
    private static string Format(SqliteDataReader reader, int i) => reader.IsDBNull(i)
        ? "n:"
        : reader.GetValue(i) switch
        {
            long l => "i:" + l.ToString(CultureInfo.InvariantCulture),
            double d => "r:" + d.ToString("R", CultureInfo.InvariantCulture),
            byte[] b => "b:" + Convert.ToBase64String(b),
            var other => "t:" + Convert.ToString(other, CultureInfo.InvariantCulture),
        };

    private static async Task<List<string>> ReadNamesAsync(SqliteConnection connection, string sql, string? table, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (table is not null)
        {
            command.Parameters.AddWithValue("@table", table);
        }

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
