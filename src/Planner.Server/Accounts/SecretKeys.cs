using System.Security.Cryptography;
using System.Text;
using Planner.Server.Data.Storage;

namespace Planner.Server.Accounts;

/// <summary>
/// Хэш кода входа (docs/specs/accounts.md, L3): HMAC-SHA256 с ключом из keys\hmac.key (32 случайных байта; создаётся при первом обращении).
/// Ключ — не в accounts.db: по одной утёкшей базе 6 цифр не подобрать.
/// </summary>
public sealed class CodeHasher(DataPaths paths)
{
    private readonly Lazy<byte[]> _key = new(() => LoadOrCreate(paths));

    public string Hash(string email, string code) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(_key.Value, Encoding.UTF8.GetBytes($"{email}:{code}")));

    /// <summary>Хэш токена приглашения (I2): в базе ссылки нет — только этот хэш.</summary>
    public string HashToken(string token) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(_key.Value, Encoding.UTF8.GetBytes($"invite:{token}")));

    private static byte[] LoadOrCreate(DataPaths paths)
    {
        if (File.Exists(paths.HmacKeyFile))
        {
            return Convert.FromBase64String(File.ReadAllText(paths.HmacKeyFile).Trim());
        }

        var key = RandomNumberGenerator.GetBytes(32);
        Directory.CreateDirectory(paths.KeysDirectory);
        File.WriteAllText(paths.HmacKeyFile, Convert.ToBase64String(key));
        return key;
    }
}
