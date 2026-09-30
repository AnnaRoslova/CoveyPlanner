using System.Text.RegularExpressions;
using Microsoft.Extensions.FileProviders;

namespace Planner.Server.Hosting;

/// <summary>
/// Отдаёт index.html клиента с подставленными именами файлов.
///
/// В index.html клиента ссылки записаны шаблонами: «_framework/blazor.webassembly#[.{fingerprint}].js».
/// При обычной сборке (dotnet run) их подставляет сама сборка — сервер получает уже готовый файл.
/// А при публикации серверного проекта (dotnet publish, tools/install.ps1) в wwwroot попадает
/// исходный index.html с шаблонами — браузер получил бы 404 и пустую страницу.
/// Поэтому, если шаблоны остались, подставляем имена сами по файлам в wwwroot — один раз при первом запросе.
/// </summary>
public sealed partial class IndexHtml(IWebHostEnvironment environment)
{
    private readonly Lazy<string> _html = new(() => Build(environment.WebRootFileProvider));

    // «путь#[.{fingerprint}].расширение», например «_framework/blazor.webassembly#[.{fingerprint}].js»
    [GeneratedRegex(@"(?<path>[\w./-]+)#\[\.\{fingerprint\}\](?<ext>(?:\.[\w-]+)+)")]
    private static partial Regex Placeholder();

    /// <summary>Готовый текст страницы.</summary>
    public string Html => _html.Value;

    public IResult Serve(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-cache";
        return Results.Content(Html, "text/html; charset=utf-8");
    }

    private static string Build(IFileProvider files)
    {
        var file = files.GetFileInfo("index.html");
        using var reader = new StreamReader(file.CreateReadStream());
        var html = reader.ReadToEnd();
        if (!html.Contains("#[.{fingerprint}]", StringComparison.Ordinal))
        {
            return html; // уже обработан сборкой
        }

        html = Placeholder().Replace(html, m => Resolve(files, m.Groups["path"].Value, m.Groups["ext"].Value));

        // Карта модулей без отпечатков не нужна, но пустой тег браузер считает ошибкой разбора
        html = html.Replace("<script type=\"importmap\"></script>", "<script type=\"importmap\">{}</script>", StringComparison.Ordinal);

        // Предзагрузка рантайма .NET (так же делает сборка): <link rel="preload" id="webassembly" />
        var dotnetJs = FindFingerprinted(files, "_framework/dotnet", ".js");
        html = html.Replace(
            "<link rel=\"preload\" id=\"webassembly\" />",
            dotnetJs is null
                ? string.Empty
                : $"<link href=\"{dotnetJs}\" rel=\"preload\" as=\"script\" fetchpriority=\"high\" crossorigin=\"anonymous\" />",
            StringComparison.Ordinal);

        return html;
    }

    /// <summary>Файл с отпечатком (blazor.webassembly.w3qd1tpl0e.js), а если такого нет — без него.</summary>
    private static string Resolve(IFileProvider files, string path, string ext) =>
        FindFingerprinted(files, path, ext) ?? path + ext;

    private static string? FindFingerprinted(IFileProvider files, string path, string ext)
    {
        var slash = path.LastIndexOf('/');
        var directory = slash < 0 ? "" : path[..slash];
        var name = slash < 0 ? path : path[(slash + 1)..];
        var pattern = new Regex($"^{Regex.Escape(name)}\\.[a-z0-9]{{10}}{Regex.Escape(ext)}$");

        var match = files.GetDirectoryContents(directory)
            .FirstOrDefault(f => !f.IsDirectory && pattern.IsMatch(f.Name));
        return match is null ? null : (directory.Length == 0 ? match.Name : $"{directory}/{match.Name}");
    }
}
