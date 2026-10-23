using Microsoft.Data.Sqlite;

namespace Planner.Tests.Api;

/// <summary>Файлы тестовых баз.</summary>
public static class TestDatabase
{
    /// <summary>
    /// Закрыть соединения пула только этой базы (строка подключения — как у сервера и тестов: «Data Source=путь»), чтобы файл можно было удалить.
    /// Не SqliteConnection.ClearAllPools(): он закрывает соединения всех баз процесса, и сервер соседнего теста,
    /// который в этот момент запускается, падает с ObjectDisposedException (batch-3.md, FT2).
    /// </summary>
    public static void ClearPool(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        SqliteConnection.ClearPool(connection);
    }

    /// <summary>Удалить папку данных или копий теста — сначала закрыв пулы всех её баз.</summary>
    public static void DeleteDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var database in Directory.GetFiles(directory, "*.db", SearchOption.AllDirectories))
        {
            ClearPool(database);
        }

        Directory.Delete(directory, recursive: true);
    }
}
