using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text;
using Planner.Server.Data.Storage;

namespace Planner.Server.Accounts;

/// <summary>Письмо: код входа или приглашение (docs/specs/accounts.md, M4) — короткое, текстом, без отслеживания.</summary>
public sealed record EmailMessage(string To, string Subject, string Body);

/// <summary>Отправка писем (M1): почтовый ящик разработки или SMTP — по настройке Mail:Mode.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

/// <summary>Режим почты (Mail:Mode).</summary>
public enum MailMode
{
    /// <summary>Письма — файлами в папку (почтовый ящик разработки), без отправки.</summary>
    Folder,

    Smtp,
}

public static class MailSettings
{
    public const string ModeKey = "Mail:Mode";

    public static MailMode Mode(IConfiguration configuration) => configuration.GetValue(ModeKey, MailMode.Folder);
}

/// <summary>Почта в журналах и именах файлов — маской: «a***@gmail.com» (C: в журналы адреса целиком не пишем).</summary>
public static class EmailMask
{
    public static string Mask(string email)
    {
        var at = email.IndexOf('@');
        return at <= 0 ? "***" : $"{email[0]}***{email[at..]}";
    }
}

/// <summary>
/// Почтовый ящик разработки (M2): письмо — файл .eml в mail\ (открывается почтовой программой); чтение — для страницы /dev/mailbox и тестов.
/// </summary>
public sealed class FolderEmailSender(DataPaths paths, TimeProvider clock, ILogger<FolderEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        Directory.CreateDirectory(paths.MailDirectory);
        var now = clock.GetLocalNow();
        var name = $"{now:yyyy-MM-dd_HHmmss_fff}_{Guid.NewGuid():N}.eml";
        var eml = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"Date: {now:R}\r\n")
            .Append(CultureInfo.InvariantCulture, $"To: {message.To}\r\n")
            .Append(CultureInfo.InvariantCulture, $"Subject: =?utf-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(message.Subject))}?=\r\n")
            .Append("Content-Type: text/plain; charset=utf-8\r\n\r\n")
            .Append(message.Body);
        await File.WriteAllTextAsync(Path.Combine(paths.MailDirectory, name), eml.ToString(), ct);
        logger.LogInformation("Письмо {To} — в почтовом ящике разработки", EmailMask.Mask(message.To));
    }
}

/// <summary>Письмо из почтового ящика разработки.</summary>
public sealed record MailboxLetter(DateTimeOffset At, string To, string Subject, string Body);

/// <summary>Чтение почтового ящика разработки — свежие сверху.</summary>
public sealed class DevMailbox(DataPaths paths)
{
    public IReadOnlyList<MailboxLetter> Latest(int count = 20, string? to = null) =>
        !Directory.Exists(paths.MailDirectory) ? []
        : Directory.GetFiles(paths.MailDirectory, "*.eml")
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .Select(Read)
            .Where(l => to is null || string.Equals(l.To, to, StringComparison.OrdinalIgnoreCase))
            .Take(count)
            .ToList();

    private static MailboxLetter Read(string file)
    {
        var text = File.ReadAllText(file);
        var split = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var headers = text[..split].Split("\r\n").Select(h => h.Split(": ", 2)).ToDictionary(h => h[0], h => h[1]);
        var subject = headers["Subject"];
        if (subject.StartsWith("=?utf-8?B?", StringComparison.Ordinal))
        {
            subject = Encoding.UTF8.GetString(Convert.FromBase64String(subject[10..^2]));
        }

        return new MailboxLetter(DateTimeOffset.Parse(headers["Date"], CultureInfo.InvariantCulture), headers["To"], subject, text[(split + 4)..]);
    }
}

/// <summary>Боевая отправка (M3): Mail:Smtp:Host, Port, User, Password, From, UseTls — в settings.json или переменных окружения, не в git.</summary>
public sealed class SmtpEmailSender(IConfiguration configuration, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var smtp = configuration.GetSection("Mail:Smtp");
        using var client = new SmtpClient(smtp["Host"], smtp.GetValue("Port", 587))
        {
            EnableSsl = smtp.GetValue("UseTls", true),
        };
        if (!string.IsNullOrEmpty(smtp["User"]))
        {
            client.Credentials = new NetworkCredential(smtp["User"], smtp["Password"]);
        }

        using var mail = new MailMessage(smtp["From"]!, message.To, message.Subject, message.Body)
        {
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8,
        };
        await client.SendMailAsync(mail, ct);
        logger.LogInformation("Письмо {To} отправлено", EmailMask.Mask(message.To));
    }
}
