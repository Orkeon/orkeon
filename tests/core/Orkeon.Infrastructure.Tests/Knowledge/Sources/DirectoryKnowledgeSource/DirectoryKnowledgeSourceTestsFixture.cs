using Orkeon.Application.Interfaces.Knowledge;
using Orkeon.Infrastructure.Knowledge.Chunking;
using Orkeon.Infrastructure.Knowledge.Loaders;
using Orkeon.Infrastructure.Knowledge.Sources;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.Knowledge.Sources;

public sealed class DirectoryKnowledgeSourceTestsFixture : IDisposable
{
    private readonly string _tempDir;
    private readonly MockFileSystemService _fs;

    public DirectoryKnowledgeSourceTestsFixture()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"knowledge_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _fs = new MockFileSystemService(_tempDir);
    }

    public DirectoryKnowledgeSourceTestsFixture WithFile(string name, string content)
    {
        File.WriteAllText(Path.Combine(_tempDir, name), content);
        return this;
    }

    /// <summary>Creates a source pointing at the virtual root "/".</summary>
    public DirectoryKnowledgeSource CreateSource()
        => new("/", _fs, CreateFactory(_fs), new RecursiveTextChunker());

    /// <summary>Creates a source pointing at the given virtual path.</summary>
    public DirectoryKnowledgeSource CreateSourceAtPath(string vPath)
        => new(vPath, _fs, CreateFactory(_fs), new RecursiveTextChunker());

    /// <summary>Gets the virtual root path used by this fixture.</summary>
    public static string GetVirtualRoot() => "/";

    private static DocumentLoaderFactory CreateFactory(MockFileSystemService fs)
    {
        var loaders = new IDocumentLoader[]
        {
            new TextFileLoader(fs),
            new CsvDocumentLoader(fs),
            new HtmlDocumentLoader(fs)
        };
        return new DocumentLoaderFactory(loaders);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch { }
    }
}
