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
