using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Planner.Server.Data.Storage;

/// <summary>Итог переноса planner.db в файл администратора (docs/specs/accounts.md, X2).</summary>
public sealed record TransferReport(
    string AdminEmail,
    string Source,
    string Target,
    IReadOnlyList<TableFingerprint> Before,
    IReadOnlyList<TableFingerprint> After,
    DateTimeOffset At)
{
    public IReadOnlyList<string> Differences => DatabaseFingerprint.Differences(Before, After);

    public bool Matches => Differences.Count == 0;

    /// <summary>Отчёт для Анны: таблица «до / после» по каждой таблице базы.</summary>
    public string ToMarkdown(IReadOnlyList<TableFingerprint>? afterStartup = null)
    {
        var text = new StringBuilder();
        text.AppendLine("# Перенос данных в аккаунт администратора").AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"- Когда: {At:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine(CultureInfo.InvariantCulture, $"- Администратор: {AdminEmail}");
        text.AppendLine(CultureInfo.InvariantCulture, $"- Откуда: `{Source}`");
        text.AppendLine(CultureInfo.InvariantCulture, $"- Куда: `{Target}`");
        text.AppendLine(CultureInfo.InvariantCulture, $"- Итог: {(Matches ? "**всё совпадает** ✓" : $"**НЕ СОВПАЛО**: {string.Join(", ", Differences)}")}");
        text.AppendLine();

        var startup = afterStartup?.ToDictionary(t => t.Table, StringComparer.Ordinal);
        text.Append("| Таблица | Строк до | Строк после | Сумма до | Сумма после | |");
        text.AppendLine(startup is null ? "" : " Строк после запуска |");
        text.Append("|---|---:|---:|---|---|---|");
        text.AppendLine(startup is null ? "" : "---:|");

        var afterByTable = After.ToDictionary(t => t.Table, StringComparer.Ordinal);
        foreach (var before in Before)
        {
            afterByTable.TryGetValue(before.Table, out var after);
            text.Append(CultureInfo.InvariantCulture,
                $"| {before.Table} | {before.Rows} | {after?.Rows.ToString(CultureInfo.InvariantCulture) ?? "—"} | {before.Hash[..12]} | {after?.Hash[..12] ?? "—"} | {(before == after ? "✓" : "✗")} |");
            text.AppendLine(startup is null ? ""
                : $" {(startup.TryGetValue(before.Table, out var s) ? s.Rows.ToString(CultureInfo.InvariantCulture) : "—")} |");
        }

        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Всего строк: {Before.Sum(t => t.Rows)} → {After.Sum(t => t.Rows)}.");
        if (startup is not null)
        {
            text.AppendLine(CultureInfo.InvariantCulture,
                $"После запуска (миграции файла пользователя): {afterStartup!.Sum(t => t.Rows)} строк.");
        }

        return text.ToString();
    }
}

/// <summary>
/// Перенос planner.db (одного пользователя, до аккаунтов) в файл администратора (X1–X2): целостная копия средствами SQLite
/// во временный файл, сверка числа строк и сумм каждой таблицы, и только потом — файл на место.
/// Не совпало — копия удаляется, planner.db не тронут, исключение (сервер не стартует).
/// </summary>
public static class LegacyTransfer
{
    public static async Task<TransferReport> CopyAsync(
        string source, string target, string adminEmail, DateTimeOffset now, CancellationToken ct = default)
    {
        var temporary = target + ".tmp";
        File.Delete(temporary);

        var before = await DatabaseFingerprint.ComputeAsync(source, ct: ct);
        await using (var connection = new SqliteConnection($"Data Source={source};Pooling=False"))
        {
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "VACUUM INTO @target";
            command.Parameters.AddWithValue("@target", temporary);
            await command.ExecuteNonQueryAsync(ct);
        }

        var after = await DatabaseFingerprint.ComputeAsync(temporary, ct: ct);
        var report = new TransferReport(adminEmail, source, target, before, after, now);
        if (!report.Matches)
        {
            File.Delete(temporary);
            throw new InvalidOperationException(
                $"Перенос {source} не сошёлся (таблицы: {string.Join(", ", report.Differences)}) — данные не тронуты.");
        }

        File.Move(temporary, target);
        return report;
    }

    /// <summary>planner.db (с журналом -wal/-shm) — в planner-before-accounts.db; такое имя уже занято — с отметкой времени.</summary>
    public static string KeepLegacy(DataPaths paths, DateTimeOffset now)
    {
        var kept = File.Exists(paths.LegacyKeptDatabase)
            ? Path.Combine(paths.Root, $"planner-before-accounts-{now:yyyy-MM-dd_HHmmss}.db")
            : paths.LegacyKeptDatabase;
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            if (File.Exists(paths.LegacyDatabase + suffix))
            {
                File.Move(paths.LegacyDatabase + suffix, kept + suffix);
            }
        }

        File.Move(paths.LegacyDatabase, kept);
        return kept;
    }
}
