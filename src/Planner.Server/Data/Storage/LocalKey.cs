using System.Security.Cryptography;
using System.Text;

namespace Planner.Server.Data.Storage;

/// <summary>
/// Ключ служебных скриптов (docs/specs/accounts.md, D6): tools/backup.ps1 и install.ps1 читают его из local.key
/// и присылают заголовком — так копию может попросить только тот, у кого есть доступ к диску сервера.
/// «Запрос с localhost» не годится: за обратным прокси все запросы приходят с localhost.
/// </summary>
public static class LocalKey
{
    public const string Header = "X-Planner-Local-Key";

    /// <summary>Ключ из файла; файла нет — создать (32 случайных байта).</summary>
    public static string Ensure(DataPaths paths)
    {
        if (File.Exists(paths.LocalKeyFile))
        {
            return File.ReadAllText(paths.LocalKeyFile).Trim();
        }

        var key = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        File.WriteAllText(paths.LocalKeyFile, key);
        return key;
    }

    public static bool Matches(HttpContext context, DataPaths paths)
    {
        if (!context.Request.Headers.TryGetValue(Header, out var sent) || !File.Exists(paths.LocalKeyFile))
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(File.ReadAllText(paths.LocalKeyFile).Trim());
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(sent.ToString()), expected);
    }
}
