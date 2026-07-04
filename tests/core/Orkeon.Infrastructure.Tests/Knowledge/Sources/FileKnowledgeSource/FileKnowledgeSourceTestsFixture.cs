using Orkeon.Application.Interfaces.Knowledge;
using Orkeon.Infrastructure.Knowledge.Chunking;
using Orkeon.Infrastructure.Knowledge.Loaders;
using Orkeon.Infrastructure.Knowledge.Sources;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.Knowledge.Sources;

public sealed class FileKnowledgeSourceTestsFixture : IDisposable
{
    private readonly string _tempDir;
    private readonly MockFileSystemService _fs;

    public FileKnowledgeSourceTestsFixture()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"file-src-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _fs = new MockFileSystemService(_tempDir);
    }

    public string CreateTempFile(string extension, string content)
    {
        var name = $"test_{Guid.NewGuid()}{extension}";
        File.WriteAllText(Path.Combine(_tempDir, name), content);
        return $"/{name}"; // virtual path
    }

    public FileKnowledgeSource CreateSource(string vPath, ChunkingOptions? chunkingOptions = null)
    {
        var loader = new TextFileLoader(_fs);
        return chunkingOptions is not null
            ? new FileKnowledgeSource(vPath, loader, new RecursiveTextChunker(), chunkingOptions)
            : new FileKnowledgeSource(vPath, loader, new RecursiveTextChunker());
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
