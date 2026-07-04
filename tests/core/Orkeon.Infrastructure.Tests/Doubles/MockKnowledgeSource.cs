using Orkeon.Domain.Common;
using Orkeon.Domain.Knowledge;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock implementation of IKnowledgeSource for testing.
/// </summary>
public sealed class MockKnowledgeSource : IKnowledgeSource
{
    private KnowledgeContent _contentResult = new()
    {
        Title = "Mock Content",
        Content = "mock knowledge content",
        Source = "mock-source"
    };

    private IEnumerable<KnowledgeContent> _searchResult = Array.Empty<KnowledgeContent>();
    private Func<string, int, IEnumerable<KnowledgeContent>>? _searchFunc;

    public KnowledgeSourceId Id { get; set; } = KnowledgeSourceId.Create();
    public string Name { get; set; } = "MockKnowledgeSource";
    public string Type { get; set; } = "mock";

    // --- Tracking ---
    public int GetContentCallCount { get; private set; }
    public int SearchCallCount { get; private set; }
    public string? LastSearchQuery { get; private set; }
    public int? LastSearchLimit { get; private set; }

    // --- Configuration ---
    public void SetContentResult(KnowledgeContent result) => _contentResult = result;
    public void SetSearchResult(IEnumerable<KnowledgeContent> result) => _searchResult = result;
    public void SetSearchFunc(Func<string, int, IEnumerable<KnowledgeContent>> func) => _searchFunc = func;

    public Task<KnowledgeContent> GetContentAsync(CancellationToken cancellationToken = default)
    {
        GetContentCallCount++;
        return Task.FromResult(_contentResult);
    }

    public Task<IEnumerable<KnowledgeContent>> SearchAsync(
        string query,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        SearchCallCount++;
        LastSearchQuery = query;
        LastSearchLimit = limit;

        var result = _searchFunc != null ? _searchFunc(query, limit) : _searchResult;
        return Task.FromResult(result);
    }

    /// <summary>
    /// Resets all tracking counters and last arguments.
    /// </summary>
    public void Reset()
    {
        GetContentCallCount = 0;
        SearchCallCount = 0;
        LastSearchQuery = null;
        LastSearchLimit = null;
    }
}
