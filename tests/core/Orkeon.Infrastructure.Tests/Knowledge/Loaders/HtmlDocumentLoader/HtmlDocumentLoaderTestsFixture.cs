using Orkeon.Application.Interfaces.Knowledge;
using Orkeon.Infrastructure.Knowledge.Loaders;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.Knowledge.Loaders;

public sealed class HtmlDocumentLoaderTestsFixture : IDisposable
{
    private readonly string _tempDir;
    private readonly MockFileSystemService _fs;
    private readonly HtmlDocumentLoader _loader;
    private readonly List<string> _tempFiles = [];

    public HtmlDocumentLoaderTestsFixture()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"html-loader-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _fs = new MockFileSystemService(_tempDir);
        _loader = new HtmlDocumentLoader(_fs);
    }

    public string CreateTempFile(string content)
    {
        var name = $"test_{Guid.NewGuid()}.html";
        var physical = Path.Combine(_tempDir, name);
        File.WriteAllText(physical, content);
        _tempFiles.Add(physical);
        return $"/{name}"; // virtual path
    }

    public Task<LoadedDocument> LoadAsync(string vPath)
        => _loader.LoadAsync(vPath);

    public bool CanLoad(string source)
        => _loader.CanLoad(source);

    public HtmlDocumentLoader GetLoader() => _loader;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
