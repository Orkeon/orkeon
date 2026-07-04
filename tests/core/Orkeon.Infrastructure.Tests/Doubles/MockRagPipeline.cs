using Orkeon.Application.Interfaces.Rag;
using Orkeon.Application.Rag;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock implementation of IRagPipeline for testing within Infrastructure.Tests.
/// </summary>
public sealed class MockRagPipeline : IRagPipeline
{
    private RagResult _executeResult = new()
    {
        Answer = "mock RAG answer",
        Sources = Array.Empty<RetrievedChunk>(),
        Metrics = new RagMetrics()
    };

    private Func<RagQuery, RagResult>? _executeFunc;

    // --- Tracking ---
    public int ExecuteQueryCallCount { get; private set; }
    public int ExecuteStringCallCount { get; private set; }
    public RagQuery? LastExecuteQuery { get; private set; }
    public string? LastExecuteQuestion { get; private set; }
    public RagOptions? LastExecuteOptions { get; private set; }

    // --- Configuration ---
    public void SetExecuteResult(RagResult result) => _executeResult = result;
    public void SetExecuteResult(string answer) => _executeResult = new RagResult { Answer = answer };
    public void SetExecuteFunc(Func<RagQuery, RagResult> func) => _executeFunc = func;

    public Task<RagResult> ExecuteAsync(RagQuery query, CancellationToken ct = default)
    {
        ExecuteQueryCallCount++;
        LastExecuteQuery = query;

        var result = _executeFunc != null ? _executeFunc(query) : _executeResult;
        return Task.FromResult(result);
    }

    public Task<RagResult> ExecuteAsync(string question, RagOptions? options = null, CancellationToken ct = default)
    {
        ExecuteStringCallCount++;
        LastExecuteQuestion = question;
        LastExecuteOptions = options;

        var query = new RagQuery
        {
            Question = question,
            Options = options ?? new RagOptions()
        };

        var result = _executeFunc != null ? _executeFunc(query) : _executeResult;
        return Task.FromResult(result);
    }

    /// <summary>
    /// Resets all tracking counters and last arguments.
    /// </summary>
    public void Reset()
    {
        ExecuteQueryCallCount = 0;
        ExecuteStringCallCount = 0;
        LastExecuteQuery = null;
        LastExecuteQuestion = null;
        LastExecuteOptions = null;
    }
}
