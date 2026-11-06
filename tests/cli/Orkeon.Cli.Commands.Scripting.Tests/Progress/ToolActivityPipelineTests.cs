using System.Diagnostics;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Constants.Llm;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Commands.Scripting.Progress;
using Orkeon.Cli.Commands.Scripting.Tests.Doubles;

namespace Orkeon.Cli.Commands.Scripting.Tests.Progress;

public sealed class ToolActivityPipelineTests : IDisposable
{
    private readonly FakeCountingDisposableTool _tool = new(new ToolInstantiationCounter());

    public void Dispose() => _tool.Dispose();

    private ToolInvocation Invocation()
        => new(_tool, new Dictionary<string, object?>(), new ToolInvocationCaller("a", "a"));

    [Fact]
    public async Task A_call_reaches_the_observer_before_and_after_the_tool()
    {
        // Arrange
        var observer = new FakeToolActivityObserver();
        var inner = new FakeToolInvocationPipeline { OnInvoke = () => observer.Seen.Add("tool") };
        var pipeline = new ToolActivityPipeline(inner, new ToolActivityFeed { Observer = observer }, new PassThroughFileSystemService());

        // Act
        await pipeline.InvokeAsync(Invocation(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["started", "tool", "finished:True"], observer.Seen);
    }

    [Fact]
    public async Task A_refused_call_ends_as_a_failure()
    {
        // Arrange
        var observer = new FakeToolActivityObserver();
        var inner = new FakeToolInvocationPipeline { Success = false };
        var pipeline = new ToolActivityPipeline(inner, new ToolActivityFeed { Observer = observer }, new PassThroughFileSystemService());

        // Act
        await pipeline.InvokeAsync(Invocation(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("finished:False", observer.Seen[^1]);
    }

    [Fact]
    public async Task A_call_that_throws_still_ends()
    {
        // Arrange
        var observer = new FakeToolActivityObserver();
        var inner = new FakeToolInvocationPipeline { OnInvoke = () => throw new InvalidOperationException("boom") };
        var pipeline = new ToolActivityPipeline(inner, new ToolActivityFeed { Observer = observer }, new PassThroughFileSystemService());

        // Act
        await Record.ExceptionAsync(() => pipeline.InvokeAsync(Invocation(), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("finished:False", observer.Seen[^1]);
    }

    [Fact]
    public async Task Without_an_observer_the_call_only_passes_through()
    {
        // Arrange
        var inner = new FakeToolInvocationPipeline();
        var pipeline = new ToolActivityPipeline(inner, new ToolActivityFeed(), new PassThroughFileSystemService());

        // Act
        await pipeline.InvokeAsync(Invocation(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Each_call_has_its_own_id()
    {
        // Arrange
        var observer = new FakeToolActivityObserver();
        var pipeline = new ToolActivityPipeline(new FakeToolInvocationPipeline(), new ToolActivityFeed { Observer = observer }, new PassThroughFileSystemService());
        await pipeline.InvokeAsync(Invocation(), TestContext.Current.CancellationToken);

        // Act
        await pipeline.InvokeAsync(Invocation(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([1L, 2L], observer.StartedIds);
    }

    [Fact]
    public async Task An_observer_that_throws_when_a_call_starts_leaves_the_call_alone()
    {
        // Arrange
        var inner = new FakeToolInvocationPipeline();
        var observer = new FakeToolActivityObserver { ThrowOnStarted = true };
        var pipeline = new ToolActivityPipeline(inner, new ToolActivityFeed { Observer = observer }, new PassThroughFileSystemService());

        // Act
        var result = await pipeline.InvokeAsync(Invocation(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((1, true), (inner.Calls, result.Success));
    }

    [Fact]
    public async Task An_observer_that_throws_when_a_call_ends_leaves_its_result_alone()
    {
        // Arrange
        var observer = new FakeToolActivityObserver { ThrowOnFinished = true };
        var pipeline = new ToolActivityPipeline(new FakeToolInvocationPipeline(), new ToolActivityFeed { Observer = observer }, new PassThroughFileSystemService());

        // Act
        var result = await pipeline.InvokeAsync(Invocation(), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public async Task The_span_of_the_call_is_marked_as_shown_by_a_step()
    {
        // Arrange
        using var sampling = ListenToTestSpans();
        using var source = new ActivitySource(SpanSource);
        using var span = source.StartActivity("tool")!;
        span.SetTag(GenAiAttributes.ToolName, _tool.Name);
        var pipeline = new ToolActivityPipeline(new FakeToolInvocationPipeline(), new ToolActivityFeed { Observer = new FakeToolActivityObserver() }, new PassThroughFileSystemService());

        // Act
        await pipeline.InvokeAsync(Invocation(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(true, span.GetTagItem("orkeon.tool.reported"));
    }

    [Fact]
    public async Task The_span_of_another_tool_around_the_call_stays_unmarked()
    {
        // Arrange
        using var sampling = ListenToTestSpans();
        using var source = new ActivitySource(SpanSource);
        using var span = source.StartActivity("tool")!;
        span.SetTag(GenAiAttributes.ToolName, "delegate");
        var pipeline = new ToolActivityPipeline(new FakeToolInvocationPipeline(), new ToolActivityFeed { Observer = new FakeToolActivityObserver() }, new PassThroughFileSystemService());

        // Act
        await pipeline.InvokeAsync(Invocation(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(span.GetTagItem("orkeon.tool.reported"));
    }

    private const string SpanSource = "ToolActivityPipelineTests";

    private static ActivityListener ListenToTestSpans()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SpanSource,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
