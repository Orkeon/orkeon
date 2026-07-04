using Microsoft.Extensions.Logging;
using Orkeon.Application.Rag;
using Orkeon.Application.Tests.Doubles;

namespace Orkeon.Application.Tests.Rag;

public class RagPipelineTests
{
    private readonly MockRetriever _retriever = new();
    private readonly MockContextAugmenter _augmenter = new();
    private readonly MockResponseGenerator _generator = new();
    private readonly RagPipelineTestLogger _logger = new();

    private RagPipeline CreatePipeline()
        => new(_retriever, _augmenter, _generator, _logger);

    private static List<RetrievedChunk> SampleChunks() =>
    [
        new() { Content = "Chunk 1", SourceId = "src1", RelevanceScore = 0.9f },
        new() { Content = "Chunk 2", SourceId = "src2", RelevanceScore = 0.8f }
    ];

    private void SetupDefaults(
        IReadOnlyList<RetrievedChunk>? chunks = null,
        AugmentedPrompt? prompt = null,
        GeneratedResponse? response = null)
    {
        chunks ??= SampleChunks();
        prompt ??= new AugmentedPrompt
        {
            SystemPrompt = "system",
            UserPrompt = "user",
            UsedChunks = chunks
        };
        response ??= new GeneratedResponse { Text = "Generated answer", TokensUsed = 42 };

        _retriever.SetRetrieveResult(chunks);

        _augmenter.SetAugmentResult(prompt);

        _generator.SetGenerateResult(response);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnResult_WhenExecutingAsyncWithFullPipeline()
    {
        SetupDefaults();
        var pipeline = CreatePipeline();

        var result = await pipeline.ExecuteAsync(
            new RagQuery { Question = "What is RAG?" }, TestContext.Current.CancellationToken);

        Assert.Equal("Generated answer", result.Answer);
        Assert.Equal(2, result.Sources.Count);
        Assert.Equal(42, result.Metrics.TokensUsed);

        Assert.Equal(1, _retriever.RetrieveCallCount);
        Assert.Equal("What is RAG?", _retriever.LastRetrieveQuery);
        Assert.Equal(1, _augmenter.AugmentCallCount);
        Assert.Equal("What is RAG?", _augmenter.LastAugmentQuestion);
        Assert.Equal(1, _generator.GenerateCallCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnEmptySourcesResult_WhenExecutingAsyncWithNoChunksRetrieved()
    {
        var emptyChunks = Array.Empty<RetrievedChunk>() as IReadOnlyList<RetrievedChunk>;
        var prompt = new AugmentedPrompt
        {
            SystemPrompt = "system",
            UserPrompt = "user",
            UsedChunks = emptyChunks
        };

        SetupDefaults(chunks: emptyChunks, prompt: prompt);
        var pipeline = CreatePipeline();

        var result = await pipeline.ExecuteAsync(
            new RagQuery { Question = "Unknown topic" }, TestContext.Current.CancellationToken);

        Assert.Empty(result.Sources);
        Assert.Equal(0, result.Metrics.TotalChunksRetrieved);
        Assert.Equal(0f, result.Metrics.AverageRelevanceScore);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldDelegateToQuery_WhenExecutingAsyncStringOverload()
    {
        SetupDefaults();
        var pipeline = CreatePipeline();

        var result = await pipeline.ExecuteAsync("Simple question", ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("Generated answer", result.Answer);

        Assert.Equal(1, _retriever.RetrieveCallCount);
        Assert.Equal("Simple question", _retriever.LastRetrieveQuery);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldContainTimings_WhenExecutingAsyncMetrics()
    {
        SetupDefaults();
        var pipeline = CreatePipeline();

        var result = await pipeline.ExecuteAsync(
            new RagQuery { Question = "Test timings" }, TestContext.Current.CancellationToken);

        Assert.True(result.Metrics.TotalTime >= TimeSpan.Zero);
        Assert.True(result.Metrics.RetrievalTime >= TimeSpan.Zero);
        Assert.True(result.Metrics.GenerationTime >= TimeSpan.Zero);
        Assert.Equal(2, result.Metrics.TotalChunksRetrieved);
        Assert.Equal(2, result.Metrics.ChunksUsedInPrompt);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldTrueWhenSourcesExist_WhenExecutingAsyncIsGrounded()
    {
        SetupDefaults();
        var pipeline = CreatePipeline();

        var result = await pipeline.ExecuteAsync("Grounded question", ct: TestContext.Current.CancellationToken);

        Assert.True(result.IsGrounded);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldFalseWhenNoSources_WhenExecutingAsyncIsGrounded()
    {
        var emptyChunks = Array.Empty<RetrievedChunk>() as IReadOnlyList<RetrievedChunk>;
        var prompt = new AugmentedPrompt
        {
            SystemPrompt = "system",
            UserPrompt = "user",
            UsedChunks = emptyChunks
        };

        SetupDefaults(chunks: emptyChunks, prompt: prompt);
        var pipeline = CreatePipeline();

        var result = await pipeline.ExecuteAsync("Ungrounded question", ct: TestContext.Current.CancellationToken);

        Assert.False(result.IsGrounded);
    }
}

/// <summary>
/// Simple logger for RagPipeline tests.
/// </summary>
internal sealed class RagPipelineTestLogger : ILogger<RagPipeline>
{
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    { }
    public bool IsEnabled(LogLevel logLevel) => true;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
}
