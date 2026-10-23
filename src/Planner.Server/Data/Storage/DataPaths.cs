namespace Planner.Server.Data.Storage;

/// <summary>
/// Где что лежит в папке данных (docs/specs/accounts.md, D1): общая база аккаунтов, файл данных на пользователя,
/// ключи, ключ служебных скриптов, настройки установки и старый planner.db (до аккаунтов).
/// </summary>
public sealed class DataPaths(string root)
{
    /// <summary>Папка данных (appsettings.json → "Storage:DataDirectory"); пусто — %LOCALAPPDATA%\CoveyPlanner.</summary>
    public const string DataDirectoryKey = "Storage:DataDirectory";

    public string Root { get; } = root;

    public string AccountsDatabase => Path.Combine(Root, "accounts.db");

    public string UsersDirectory => Path.Combine(Root, "users");

    /// <summary>Ключи Data Protection и ключ хэшей кодов (hmac.key) — в бэкап, но не в git (D5, D8).</summary>
    public string KeysDirectory => Path.Combine(Root, "keys");

    /// <summary>Ключ служебных скриптов (D6).</summary>
    public string LocalKeyFile => Path.Combine(Root, "local.key");

    /// <summary>Настройки этой установки вне git: почта администратора, SMTP (D1).</summary>
    public string SettingsFile => Path.Combine(Root, "settings.json");

    /// <summary>База единственного пользователя — как было до аккаунтов.</summary>
    public string LegacyDatabase => Path.Combine(Root, "planner.db");

    /// <summary>Куда переименовывается planner.db после переноса (X1).</summary>
    public string LegacyKeptDatabase => Path.Combine(Root, "planner-before-accounts.db");

    /// <summary>Отчёт о переносе planner.db в аккаунт администратора (X2).</summary>
    public string TransferReport => Path.Combine(Root, "accounts-transfer-report.md");

    public string UserDatabase(Guid userId) => Path.Combine(UsersDirectory, $"{userId:N}.db");

    /// <summary>Файлы данных пользователей, которые уже есть на диске.</summary>
    public IReadOnlyList<string> ExistingUserDatabases() =>
        Directory.Exists(UsersDirectory) ? Directory.GetFiles(UsersDirectory, "*.db").Order(StringComparer.Ordinal).ToList() : [];

    public static string ConnectionString(string databasePath) => $"Data Source={databasePath}";

    /// <summary>Папка из настроек или по умолчанию; создаётся.</summary>
    public static DataPaths FromConfiguration(IConfiguration configuration) => new(ResolveRoot(configuration));

    public static string ResolveRoot(IConfiguration configuration)
    {
        var root = configuration[DataDirectoryKey];
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoveyPlanner");
        }

        root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(root));
        Directory.CreateDirectory(root);
        return root;
    }
}
