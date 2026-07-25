using Orkeon.Application.Rag;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Common;
using Orkeon.Domain.Knowledge;
using Orkeon.Infrastructure.Knowledge;
using Orkeon.Infrastructure.Knowledge.Chunking;
using Orkeon.Infrastructure.Memory;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Knowledge;

public sealed class KnowledgeServiceTestsFixture : IDisposable
{
    private static readonly string[] s_sourceA = ["source-a"];

    private readonly RecursiveTextChunker _chunker;
    // Pass-through VFS (delegates to real disk) so export/import round-trips and the
    // tests' File.Exists assertions on temp paths hold.
    private readonly PassThroughFileSystemService _fs = new();
    // Deterministic lexical embeddings + real in-memory vector store, so cosine search
    // behaves like term overlap and the lexical expectations of these tests still hold.
    private readonly StubLexicalEmbeddingProvider _embeddings = new();
    private readonly List<string> _tempFiles = [];

    public KnowledgeService Service { get; }

    public KnowledgeServiceTestsFixture()
    {
        _chunker = new RecursiveTextChunker();
        Service = new KnowledgeService(_chunker, _fs, _embeddings, new InMemoryProvider());
    }

    public static MockKnowledgeSource CreateMockSource(string name, string content, string type = "test")
    {
        var mock = new MockKnowledgeSource
        {
            Id = KnowledgeSourceId.Create(),
            Name = name,
            Type = type
        };
        mock.SetContentResult(new KnowledgeContent
        {
            Id = KnowledgeContentId.Create(),
            Title = name,
            Content = content,
            Source = name,
            Metadata = new Dictionary<string, object> { ["type"] = type }
        });
        mock.SetSearchResult([]);
        return mock;
    }

    public Task<string> AddSourceAsync(MockKnowledgeSource source, bool loadImmediately = true)
        => Service.AddSourceAsync(source, loadImmediately: loadImmediately);

    public Task<KnowledgeStatistics> GetStatisticsAsync()
        => Service.GetStatisticsAsync();

    public Task<IReadOnlyList<KnowledgeItem>> SearchAsync(string query, float minSimilarity = 0.0f, string[]? sources = null)
        => Service.SearchAsync(query, minSimilarity: minSimilarity, sources: sources);

    public static string[] GetSourceAFilter() => s_sourceA;

    public Task<KnowledgeContext> GetContextAsync(string query, string? agentId = null, Dictionary<string, object>? filters = null)
        => Service.GetContextAsync(query, agentId: agentId, filters: filters);

    public Task<string> AddKnowledgeAsync(string content, Dictionary<string, object>? metadata = null, string? source = null)
        => Service.AddKnowledgeAsync(content, metadata: metadata, source: source);

    public Task<bool> UpdateKnowledgeAsync(string id, string content)
        => Service.UpdateKnowledgeAsync(id, content);

    public Task<bool> DeleteKnowledgeAsync(string id)
        => Service.DeleteKnowledgeAsync(id);

    public Task<bool> RemoveSourceAsync(string name)
        => Service.RemoveSourceAsync(name);

    public Task<KnowledgeRefreshResult> RefreshSourcesAsync()
        => Service.RefreshSourcesAsync();

    public string CreateExportPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"knowledge_export_{Guid.NewGuid()}.json");
        _tempFiles.Add(path);
        return path;
    }

    public Task ExportAsync(string path) => Service.ExportAsync(path);

    public KnowledgeService CreateNewService() => new(_chunker, _fs, _embeddings, new InMemoryProvider());

    public Task<int> ImportAsync(string path) => Service.ImportAsync(path);

    public Task LoadSourceAsync(string name) => Service.LoadSourceAsync(name);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        foreach (var path in _tempFiles)
        {
            try { File.Delete(path); } catch { }
        }
    }
}
