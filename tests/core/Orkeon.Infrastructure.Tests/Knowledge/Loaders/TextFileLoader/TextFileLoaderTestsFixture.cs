using Orkeon.Application.Interfaces.Knowledge;
using Orkeon.Infrastructure.Knowledge.Loaders;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.Knowledge.Loaders;

public sealed class TextFileLoaderTestsFixture : IDisposable
{
    private readonly string _tempDir;
    private readonly MockFileSystemService _fs;
    private readonly TextFileLoader _loader;

    public TextFileLoaderTestsFixture()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"text-loader-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _fs = new MockFileSystemService(_tempDir);
        _loader = new TextFileLoader(_fs);
    }

    public string CreateTempFile(string extension, string content)
    {
        var name = $"test_{Guid.NewGuid()}{extension}";
        File.WriteAllText(Path.Combine(_tempDir, name), content);
        return $"/{name}"; // virtual path
    }

    public Task<LoadedDocument> LoadAsync(string vPath)
        => _loader.LoadAsync(vPath);

    public bool CanLoad(string source)
        => _loader.CanLoad(source);

    public TextFileLoader GetLoader() => _loader;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
