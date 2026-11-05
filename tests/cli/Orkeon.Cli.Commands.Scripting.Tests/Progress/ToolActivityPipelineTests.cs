using Orkeon.Tests.Shared.FileSystem;
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
}
