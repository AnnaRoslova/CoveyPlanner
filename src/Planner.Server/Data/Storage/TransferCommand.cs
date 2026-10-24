namespace Planner.Server.Data.Storage;

/// <summary>
/// Перенос на копии без запуска сервера (docs/specs/accounts.md, X3):
/// <code>Planner.Server.exe --MigrateAccounts:Report=отчёт.md --Storage:DataDirectory=копия --Accounts:AdminEmail=почта --Storage:DailyBackup=false</code>
/// Делает то же, что запуск сервера (снимок, миграции, администратор, перенос planner.db), и пишет отчёт: числа и суммы до/после.
/// </summary>
public static class TransferCommand
{
    /// <summary>Путь к отчёту; задан — сервер не запускается, только перенос.</summary>
    public const string ReportKey = "MigrateAccounts:Report";

    /// <summary>Путь к отчёту сверки миграций на копии папки данных; задан — сервер не запускается.</summary>
    public const string CheckReportKey = "CheckMigrations:Report";

    /// <summary>
    /// Миграции на копии (правило проекта: «миграции — сначала на копии со сверкой»): для accounts.db и файла каждого пользователя —
    /// отпечаток до, запуск хранилища (снимок, миграции), отпечаток после по прежним таблицам и столбцам (новые не в счёт;
    /// история миграций — отдельно). Всё совпало — код 0.
    /// <code>Planner.Server.exe --CheckMigrations:Report=отчёт.md --Storage:DataDirectory=копия --Storage:DailyBackup=false</code>
    /// </summary>
    public static async Task<int> CheckMigrationsAsync(IServiceProvider services, string reportPath)
    {
        var paths = services.GetRequiredService<DataPaths>();
        var files = new[] { paths.AccountsDatabase }.Where(File.Exists).Concat(paths.ExistingUserDatabases()).ToList();
        var before = new Dictionary<string, (IReadOnlyDictionary<string, IReadOnlyList<string>> Schema, IReadOnlyList<TableFingerprint> Tables)>();
        foreach (var file in files)
        {
            var schema = (await DatabaseFingerprint.SchemaAsync(file)).Where(t => !t.Key.StartsWith("__EF", StringComparison.Ordinal))
                .ToDictionary(t => t.Key, t => t.Value);
            before[file] = (schema, await DatabaseFingerprint.ComputeAsync(file, schema));
        }

        var text = new System.Text.StringBuilder("# Сверка миграций на копии\n\n");
        var ok = true;
        try
        {
            await services.InitializeStorageAsync();
            foreach (var file in files)
            {
                var (schema, tables) = before[file];
                var after = await DatabaseFingerprint.ComputeAsync(file, schema);
                var differences = DatabaseFingerprint.Differences(tables, after);
                ok &= differences.Count == 0;
                var newSchema = await DatabaseFingerprint.SchemaAsync(file);
                var added = newSchema.SelectMany(t => t.Value.Where(c => !schema.TryGetValue(t.Key, out var old) || !old.Contains(c))
                    .Select(c => $"{t.Key}.{c}")).Where(c => !c.StartsWith("__EF", StringComparison.Ordinal)).ToList();
                text.Append($"## {Path.GetRelativePath(paths.Root, file)}\n\n")
                    .Append(differences.Count == 0 ? "**всё совпадает** ✓" : $"**НЕ СОВПАЛО**: {string.Join(", ", differences)}")
                    .Append($" · строк {tables.Sum(t => t.Rows)} → {after.Sum(t => t.Rows)}")
                    .Append(added.Count == 0 ? "" : $" · новое: {string.Join(", ", added)}").Append("\n\n")
                    .Append("| Таблица | Строк до | Строк после | |\n|---|---:|---:|---|\n");
                var afterByTable = after.ToDictionary(t => t.Table);
                foreach (var table in tables)
                {
                    afterByTable.TryGetValue(table.Table, out var a);
                    text.Append($"| {table.Table} | {table.Rows} | {a?.Rows.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—"} | {(a == table ? "✓" : "✗")} |\n");
                }

                text.Append('\n');
            }
        }
        catch (Exception exception)
        {
            ok = false;
            text.Append($"Запуск хранилища не удался: {exception.Message}\n");
        }

        await File.WriteAllTextAsync(reportPath, text.ToString());
        Console.WriteLine(text);
        return ok ? 0 : 1;
    }

    public static async Task<int> RunAsync(IServiceProvider services, string reportPath)
    {
        var paths = services.GetRequiredService<DataPaths>();
        string text;
        int exitCode;
        try
        {
            var report = await services.InitializeStorageAsync();
            if (report is null)
            {
                text = $"# Перенос не нужен\n\nВ {paths.Root} уже есть пользователи или нет planner.db — ничего не переносилось.\n";
                exitCode = 0;
            }
            else
            {
                // Строки в файле администратора после запуска (миграции файла, если они были)
                var afterStartup = await DatabaseFingerprint.ComputeAsync(report.Target);
                text = report.ToMarkdown(afterStartup);
                exitCode = report.Matches ? 0 : 1;
            }
        }
        catch (Exception exception)
        {
            text = $"# Перенос не удался\n\n{exception.Message}\n";
            exitCode = 1;
        }

        await File.WriteAllTextAsync(reportPath, text);
        Console.WriteLine(text);
        return exitCode;
    }
}
