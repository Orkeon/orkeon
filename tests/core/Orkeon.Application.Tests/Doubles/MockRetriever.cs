using Orkeon.Application.Interfaces.Rag;
using Orkeon.Application.Rag;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>
/// Manual mock implementation of IRetriever for testing.
/// </summary>
public sealed class MockRetriever : IRetriever
{
    private IReadOnlyList<RetrievedChunk> _retrieveResult = Array.Empty<RetrievedChunk>();
    private Func<string, RetrievalOptions, IReadOnlyList<RetrievedChunk>>? _retrieveFunc;

    // --- Tracking ---
    public int RetrieveCallCount { get; private set; }
    public string? LastRetrieveQuery { get; private set; }
    public RetrievalOptions? LastRetrieveOptions { get; private set; }

    // --- Configuration ---
    public void SetRetrieveResult(IReadOnlyList<RetrievedChunk> result) => _retrieveResult = result;
    public void SetRetrieveResult(params RetrievedChunk[] chunks) => _retrieveResult = chunks;
    public void SetRetrieveFunc(Func<string, RetrievalOptions, IReadOnlyList<RetrievedChunk>> func) =>
        _retrieveFunc = func;

    public System.Threading.Tasks.Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(
        string query,
        RetrievalOptions options,
        CancellationToken ct = default)
    {
        RetrieveCallCount++;
        LastRetrieveQuery = query;
        LastRetrieveOptions = options;

        var result = _retrieveFunc != null ? _retrieveFunc(query, options) : _retrieveResult;
        return System.Threading.Tasks.Task.FromResult(result);
    }

    /// <summary>
    /// Resets all tracking counters and last arguments.
    /// </summary>
    public void Reset()
    {
        RetrieveCallCount = 0;
        LastRetrieveQuery = null;
        LastRetrieveOptions = null;
    }
}
