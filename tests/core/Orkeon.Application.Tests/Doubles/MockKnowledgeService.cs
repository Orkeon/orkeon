using Orkeon.Application.Rag;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Knowledge;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>
/// Manual mock implementation of IKnowledgeService for testing.
/// </summary>
public sealed class MockKnowledgeService : IKnowledgeService
{
    private string _addSourceResult = "mock-source-id";
    private bool _removeSourceResult = true;
    private int _loadSourceResult = 5;
    private IReadOnlyList<KnowledgeItem> _searchResult = Array.Empty<KnowledgeItem>();
    private KnowledgeContext _contextResult = new();
    private string _addKnowledgeResult = "mock-knowledge-id";
    private bool _updateKnowledgeResult = true;
    private bool _deleteKnowledgeResult = true;
    private KnowledgeStatistics _statisticsResult = new();
    private KnowledgeRefreshResult _refreshResult = new();

    // --- Tracking ---
    public int AddSourceCallCount { get; private set; }
    public int RemoveSourceCallCount { get; private set; }
    public int LoadSourceCallCount { get; private set; }
    public int SearchCallCount { get; private set; }
    public int GetContextCallCount { get; private set; }
    public int AddKnowledgeCallCount { get; private set; }
    public int UpdateKnowledgeCallCount { get; private set; }
    public int DeleteKnowledgeCallCount { get; private set; }
    public int GetStatisticsCallCount { get; private set; }
    public int RefreshSourcesCallCount { get; private set; }
    public int ExportCallCount { get; private set; }
    public int ImportCallCount { get; private set; }
    public IKnowledgeSource? LastAddSourceSource { get; private set; }
    public string? LastRemoveSourceName { get; private set; }
    public string? LastLoadSourceName { get; private set; }
    public string? LastSearchQuery { get; private set; }
    public int? LastSearchTopK { get; private set; }
    public IDictionary<string, object>? LastSearchFilters { get; private set; }
    public string? LastGetContextQuery { get; private set; }
    public string? LastAddKnowledgeContent { get; private set; }
    public string? LastUpdateKnowledgeId { get; private set; }
    public string? LastDeleteKnowledgeId { get; private set; }
    public string? LastExportFilePath { get; private set; }
    public string? LastImportFilePath { get; private set; }

    // --- Configuration ---
    public void SetAddSourceResult(string result) => _addSourceResult = result;
    public void SetRemoveSourceResult(bool result) => _removeSourceResult = result;
    public void SetLoadSourceResult(int result) => _loadSourceResult = result;
    public void SetSearchResult(IReadOnlyList<KnowledgeItem> result) => _searchResult = result;
    public void SetContextResult(KnowledgeContext result) => _contextResult = result;
    public void SetAddKnowledgeResult(string result) => _addKnowledgeResult = result;
    public void SetUpdateKnowledgeResult(bool result) => _updateKnowledgeResult = result;
    public void SetDeleteKnowledgeResult(bool result) => _deleteKnowledgeResult = result;
    public void SetStatisticsResult(KnowledgeStatistics result) => _statisticsResult = result;
    public void SetRefreshResult(KnowledgeRefreshResult result) => _refreshResult = result;

    public System.Threading.Tasks.Task<string> AddSourceAsync(
        IKnowledgeSource source,
        bool loadImmediately = true,
        CancellationToken cancellationToken = default)
    {
        AddSourceCallCount++;
        LastAddSourceSource = source;
        return System.Threading.Tasks.Task.FromResult(_addSourceResult);
    }

    public System.Threading.Tasks.Task<bool> RemoveSourceAsync(
        string sourceName,
        CancellationToken cancellationToken = default)
    {
        RemoveSourceCallCount++;
        LastRemoveSourceName = sourceName;
        return System.Threading.Tasks.Task.FromResult(_removeSourceResult);
    }

    public System.Threading.Tasks.Task<int> LoadSourceAsync(
        string sourceName,
        KnowledgeLoadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        LoadSourceCallCount++;
        LastLoadSourceName = sourceName;
        return System.Threading.Tasks.Task.FromResult(_loadSourceResult);
    }

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
        LastSearchFilters = filters;
        return System.Threading.Tasks.Task.FromResult(_searchResult);
    }

    public System.Threading.Tasks.Task<KnowledgeContext> GetContextAsync(
        string query,
        string? agentId = null,
        Dictionary<string, object>? filters = null,
        CancellationToken cancellationToken = default)
    {
        GetContextCallCount++;
        LastGetContextQuery = query;
        return System.Threading.Tasks.Task.FromResult(_contextResult);
    }

    public System.Threading.Tasks.Task<string> AddKnowledgeAsync(
        string content,
        Dictionary<string, object>? metadata = null,
        string? source = null,
        CancellationToken cancellationToken = default)
    {
        AddKnowledgeCallCount++;
        LastAddKnowledgeContent = content;
        return System.Threading.Tasks.Task.FromResult(_addKnowledgeResult);
    }

    public System.Threading.Tasks.Task<bool> UpdateKnowledgeAsync(
        string id,
        string content,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        UpdateKnowledgeCallCount++;
        LastUpdateKnowledgeId = id;
        return System.Threading.Tasks.Task.FromResult(_updateKnowledgeResult);
    }

    public System.Threading.Tasks.Task<bool> DeleteKnowledgeAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        DeleteKnowledgeCallCount++;
        LastDeleteKnowledgeId = id;
        return System.Threading.Tasks.Task.FromResult(_deleteKnowledgeResult);
    }

    public System.Threading.Tasks.Task<KnowledgeStatistics> GetStatisticsAsync(
        CancellationToken cancellationToken = default)
    {
        GetStatisticsCallCount++;
        return System.Threading.Tasks.Task.FromResult(_statisticsResult);
    }

    public System.Threading.Tasks.Task<KnowledgeRefreshResult> RefreshSourcesAsync(
        CancellationToken cancellationToken = default)
    {
        RefreshSourcesCallCount++;
        return System.Threading.Tasks.Task.FromResult(_refreshResult);
    }

    public System.Threading.Tasks.Task ExportAsync(
        string filePath,
        KnowledgeExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ExportCallCount++;
        LastExportFilePath = filePath;
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task<int> ImportAsync(
        string filePath,
        KnowledgeImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ImportCallCount++;
        LastImportFilePath = filePath;
        return System.Threading.Tasks.Task.FromResult(0);
    }

    /// <summary>
    /// Resets all tracking counters and last arguments.
    /// </summary>
    public void Reset()
    {
        AddSourceCallCount = 0;
        RemoveSourceCallCount = 0;
        LoadSourceCallCount = 0;
        SearchCallCount = 0;
        GetContextCallCount = 0;
        AddKnowledgeCallCount = 0;
        UpdateKnowledgeCallCount = 0;
        DeleteKnowledgeCallCount = 0;
        GetStatisticsCallCount = 0;
        RefreshSourcesCallCount = 0;
        ExportCallCount = 0;
        ImportCallCount = 0;
        LastAddSourceSource = null;
        LastRemoveSourceName = null;
        LastLoadSourceName = null;
        LastSearchQuery = null;
        LastSearchTopK = null;
        LastSearchFilters = null;
        LastGetContextQuery = null;
        LastAddKnowledgeContent = null;
        LastUpdateKnowledgeId = null;
        LastDeleteKnowledgeId = null;
        LastExportFilePath = null;
        LastImportFilePath = null;
    }
}
