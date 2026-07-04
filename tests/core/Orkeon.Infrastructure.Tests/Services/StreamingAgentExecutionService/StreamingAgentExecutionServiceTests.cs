using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Context;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Task;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using System.Runtime.CompilerServices;
using Orkeon.Infrastructure.Agent;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using DomainTask = Orkeon.Domain.Task.CrewTask;

namespace Orkeon.Infrastructure.Tests.Services;

public sealed class StreamingAgentExecutionServiceTests : IDisposable
{
    private readonly MockChatClient _mockChatClient;
    private readonly StreamingTestLogger _mockLogger;
    private readonly MockMemoryScope _mockMemoryScope;
    private readonly StreamingAgentExecutionService _service;

    public StreamingAgentExecutionServiceTests()
    {
        _mockChatClient = new MockChatClient();
        _mockLogger = new StreamingTestLogger();
        _mockMemoryScope = new MockMemoryScope();
        _service = new StreamingAgentExecutionService(
            _mockChatClient,
            [],
            _mockLogger,
            new FakeFileSystemService());
    }

    [Fact]
    public void ShouldThrow_WhenConstructorWithNullChatClient()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new StreamingAgentExecutionService(null!, [], _mockLogger, new FakeFileSystemService()));
    }

    [Fact]
    public void ShouldThrow_WhenConstructorWithNullLogger()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new StreamingAgentExecutionService(_mockChatClient, [], null!, new FakeFileSystemService()));
    }

    [Fact]
    public async Task ShouldEmitStartingThought_WhenStreamExecutionAsync()
    {
        var agent = CreateAgent();
        var task = CreateTask();
        var context = CreateContext();
        SetupMockStreaming("Hello world");

        var thoughts = await CollectThoughts(agent, task, context);

        Assert.True(thoughts.Count >= 2);
        Assert.Equal(AgentThought.ThoughtType.Reasoning, thoughts[0].Type);
        Assert.Contains("Starting task", thoughts[0].Content);
    }

    [Fact]
    public async Task ShouldEmitConclusionThought_WhenStreamExecutionAsync()
    {
        var agent = CreateAgent();
        var task = CreateTask();
        var context = CreateContext();
        SetupMockStreaming("Final answer");

        var thoughts = await CollectThoughts(agent, task, context);

        var conclusion = thoughts.LastOrDefault(t => t.Type == AgentThought.ThoughtType.Conclusion);
        Assert.NotNull(conclusion);
        Assert.Contains("Final answer", conclusion!.Content);
    }

    [Fact]
    public async Task ShouldStreamTokens_WhenStreamExecutionAsync()
    {
        var agent = CreateAgent();
        var task = CreateTask();
        var context = CreateContext();

        // Setup streaming with multiple chunks
        SetupMockStreaming("chunk1", "chunk2", "chunk3");

        var thoughts = await CollectThoughts(agent, task, context);

        var reasoningThoughts = thoughts.Where(t =>
            t.Type == AgentThought.ThoughtType.Reasoning && !t.Content.Contains("Starting task")).ToList();
        Assert.Equal(3, reasoningThoughts.Count);
        Assert.Equal("chunk1", reasoningThoughts[0].Content);
        Assert.Equal("chunk2", reasoningThoughts[1].Content);
        Assert.Equal("chunk3", reasoningThoughts[2].Content);
    }

    [Fact]
    public async Task ShouldStop_WhenStreamExecutionAsyncWithCancellation()
    {
        var agent = CreateAgent();
        var task = CreateTask();
        var context = CreateContext();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _mockChatClient.SetStreamingFunc((msgs, opts, ct) => AsyncEnumerableEmpty(ct));

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in _service.StreamExecutionAsync(agent, task, context, cts.Token)) { }
        });
    }

    #region Helpers

    private static DomainAgent CreateAgent()
    {
        return new AgentBuilder()
            .Role("Researcher")
            .Goal("Research topics")
            .Backstory("Expert researcher")
            .Build();
    }

    private static DomainTask CreateTask()
    {
        return new CrewTaskBuilder()
            .Description("Research AI trends")
            .ExpectedOutput("Summary of AI trends")
            .Build();
    }

    private SimpleExecutionContext CreateContext()
    {
        return new SimpleExecutionContext(
            new Orkeon.Domain.Common.CrewId(),
            [],
            _mockMemoryScope,
            [],
            CancellationToken.None);
    }

    private void SetupMockStreaming(params string[] chunks)
    {
        _mockChatClient.SetStreamingFunc((msgs, opts, ct) => CreateAsyncEnumerable(chunks, ct));
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> CreateAsyncEnumerable(
        string[] chunks,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var chunk in chunks)
        {
            ct.ThrowIfCancellationRequested();
            yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> AsyncEnumerableEmpty(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    private async Task<List<AgentThought>> CollectThoughts(
        DomainAgent agent, DomainTask task, SimpleExecutionContext context)
    {
        var thoughts = new List<AgentThought>();
        await foreach (var thought in _service.StreamExecutionAsync(agent, task, context))
        {
            thoughts.Add(thought);
        }
        return thoughts;
    }

    #endregion

    public void Dispose()
    {
        _mockChatClient.Dispose();
        _mockMemoryScope.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Local logger for StreamingAgentExecutionServiceTests since Tests.Shared is not referenced.
/// </summary>
internal sealed class StreamingTestLogger : ILogger<StreamingAgentExecutionService>
{
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    { }

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
}
