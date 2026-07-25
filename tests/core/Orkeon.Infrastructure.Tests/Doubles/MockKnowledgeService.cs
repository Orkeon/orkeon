using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Rag;
using Orkeon.Domain.Knowledge;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual spy implementation of <see cref="IKnowledgeService"/> for Infrastructure tests.
/// Records the arguments of <see cref="SearchAsync"/> (notably the <c>filters</c> parameter,
/// RAG-01/C5) and returns a configurable result list.
/// </summary>
public sealed class MockKnowledgeService : IKnowledgeService
{
    private IReadOnlyList<KnowledgeItem> _searchResult = [];

    // --- Tracking ---
    public int SearchCallCount { get; private set; }
    public string? LastSearchQuery { get; private set; }
    public int? LastSearchTopK { get; private set; }
    public double? LastSearchMinSimilarity { get; private set; }
    public string[]? LastSearchSources { get; private set; }
    public IDictionary<string, object>? LastSearchFilters { get; private set; }

    // --- Configuration ---
    public void SetSearchResult(IReadOnlyList<KnowledgeItem> result) => _searchResult = result;

    public System.Threading.Tasks.Task<IReadOnlyList<KnowledgeItem>> SearchAsync(
        string query,
        int topK = 5,
        double minSimilarity = 0.7,
        string[]? sources = null,
        IDictionary<string, object>? filters = null,
        CancellationToken cancellationToken = default)
    {
        SearchCallCount++;
        LastSearchQuery = query;
        LastSearchTopK = topK;
        LastSearchMinSimilarity = minSimilarity;
        LastSearchSources = sources;
        LastSearchFilters = filters;
        return System.Threading.Tasks.Task.FromResult(_searchResult);
    }

    public System.Threading.Tasks.Task<string> AddSourceAsync(
        IKnowledgeSource source,
        bool loadImmediately = true,
        CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(source.Name);

    public System.Threading.Tasks.Task<bool> RemoveSourceAsync(
        string sourceName,
        CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(false);

    public System.Threading.Tasks.Task<int> LoadSourceAsync(
        string sourceName,
        KnowledgeLoadOptions? options = null,
        CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(0);

    public System.Threading.Tasks.Task<KnowledgeContext> GetContextAsync(
        string query,
        string? agentId = null,
        Dictionary<string, object>? filters = null,
        CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(new KnowledgeContext());

    public System.Threading.Tasks.Task<string> AddKnowledgeAsync(
        string content,
        Dictionary<string, object>? metadata = null,
        string? source = null,
        CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(Guid.NewGuid().ToString());

    public System.Threading.Tasks.Task<bool> UpdateKnowledgeAsync(
        string id,
        string content,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(false);

    public System.Threading.Tasks.Task<bool> DeleteKnowledgeAsync(
        string id,
        CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(false);

    public System.Threading.Tasks.Task<KnowledgeStatistics> GetStatisticsAsync(
        CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(new KnowledgeStatistics());

    public System.Threading.Tasks.Task<KnowledgeRefreshResult> RefreshSourcesAsync(
        CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(new KnowledgeRefreshResult());

    public System.Threading.Tasks.Task ExportAsync(
        string filePath,
        KnowledgeExportOptions? options = null,
        CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.CompletedTask;

    public System.Threading.Tasks.Task<int> ImportAsync(
        string filePath,
        KnowledgeImportOptions? options = null,
        CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(0);
}
