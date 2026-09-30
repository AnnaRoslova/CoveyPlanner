using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Planner.Server.Hosting;

namespace Planner.Tests.Api;

/// <summary>Подстановка имён файлов в index.html опубликованной сборки (там шаблоны не заменены).</summary>
public sealed class IndexHtmlTests : IDisposable
{
    private readonly string _webRoot = Path.Combine(Path.GetTempPath(), "planner-tests", $"wwwroot-{Guid.NewGuid():N}");

    private sealed class FakeEnvironment(string webRoot) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = webRoot;
        public IFileProvider WebRootFileProvider { get; set; } = new PhysicalFileProvider(webRoot);
        public string ApplicationName { get; set; } = "Planner";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = webRoot;
        public string EnvironmentName { get; set; } = "Production";
    }

    private string Render() => new IndexHtml(new FakeEnvironment(_webRoot)).Html;

    private void Write(string path, string content = "")
    {
        var full = Path.Combine(_webRoot, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [Fact]
    public void Published_PlaceholdersReplacedByFilesOnDisk()
    {
        Write("index.html", """
            <link rel="preload" id="webassembly" />
            <link rel="stylesheet" href="Planner.Client#[.{fingerprint}].styles.css" />
            <script type="importmap"></script>
            <script src="_framework/blazor.webassembly#[.{fingerprint}].js"></script>
            """);
        Write("_framework/blazor.webassembly.w3qd1tpl0e.js");
        Write("_framework/dotnet.vmpyz9tllu.js");
        Write("_framework/dotnet.native.vzj2a6aakt.js");
        Write("Planner.Client.styles.css"); // стили публикуются без отпечатка

        var html = Render();

        Assert.DoesNotContain("fingerprint", html);
        Assert.Contains("src=\"_framework/blazor.webassembly.w3qd1tpl0e.js\"", html);
        Assert.Contains("href=\"Planner.Client.styles.css\"", html);
        Assert.Contains("<script type=\"importmap\">{}</script>", html);
        Assert.Contains("href=\"_framework/dotnet.vmpyz9tllu.js\" rel=\"preload\"", html); // не dotnet.native
    }

    [Fact]
    public void AlreadyProcessed_ServedAsIs()
    {
        const string processed = "<script src=\"_framework/blazor.webassembly.w3qd1tpl0e.js\"></script>";
        Write("index.html", processed);

        Assert.Equal(processed, Render());
    }

    public void Dispose()
    {
        if (Directory.Exists(_webRoot))
        {
            Directory.Delete(_webRoot, recursive: true);
        }
    }
}
