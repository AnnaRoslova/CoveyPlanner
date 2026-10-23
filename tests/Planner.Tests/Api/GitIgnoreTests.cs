using System.Diagnostics;

namespace Planner.Tests.Api;

/// <summary>
/// Ключи и базы не попадают в git (docs/specs/accounts.md, D8): правка .gitignore, которая их откроет, роняет тест.
/// Пути проверяются и в корне, и во вложенной папке — на случай, если папку данных положат внутрь репозитория.
/// </summary>
public class GitIgnoreTests
{
    [Theory]
    [InlineData("local.key")]
    [InlineData("data/local.key")]
    [InlineData("keys/hmac.key")]
    [InlineData("data/keys/hmac.key")]
    [InlineData("data/keys/key-0b1f.xml")]
    [InlineData("accounts.db")]
    [InlineData("data/accounts.db")]
    [InlineData("data/users/0123456789abcdef0123456789abcdef.db")]
    [InlineData("data/users/0123456789abcdef0123456789abcdef.db-wal")]
    [InlineData("data/planner.db-shm")]
    [InlineData("data/settings.json")]
    public void SecretOrDataFile_IsIgnored(string path)
    {
        var start = new ProcessStartInfo("git", ["check-ignore", "--no-index", "-q", path])
        {
            WorkingDirectory = RepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var git = Process.Start(start)!;
        git.WaitForExit();

        Assert.True(git.ExitCode == 0, $"{path} не в .gitignore (git check-ignore: {git.ExitCode} {git.StandardError.ReadToEnd()})");
    }

    [Theory]
    [InlineData("src/Planner.Server/appsettings.json")]
    [InlineData(".vscode/settings.json")]
    public void ProjectSettings_AreNotIgnored(string path)
    {
        var start = new ProcessStartInfo("git", ["check-ignore", "--no-index", "-q", path]) { WorkingDirectory = RepositoryRoot() };
        using var git = Process.Start(start)!;
        git.WaitForExit();

        Assert.Equal(1, git.ExitCode);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Не нашёлся корень репозитория (.git).");
    }
}
