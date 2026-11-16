using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Context;
using Orkeon.Application.Crew;
using Orkeon.Application.Crew.DeliverableResolvers;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tests.Shared.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;

namespace Orkeon.Application.Tests.Crew;

/// <summary>
/// #392: a task whose <c>tool_call</c> deliverable does not exist when it ends stays a success but says
/// so (<see cref="Orkeon.Application.Interfaces.Services.TaskResult.DeliverableMissing"/>, a warning),
/// and a deliverable known to exist is named (<see cref="Orkeon.Application.Interfaces.Services.TaskResult.DeliverablePath"/>).
/// </summary>
public sealed class ExecutionOrchestratorDeliverableTests : IDisposable
{
    private const string NotesPath = "/output/notes.md";

    private readonly StubToolThenAnswerChatClient _chatClient = new(0, "The notes, in full.");

    public void Dispose() => _chatClient.Dispose();

    private static CrewTask Task(DeliverableSource? source)
    {
        var task = new CrewTaskBuilder()
            .Description("Take notes on the lecture")
            .ExpectedOutput("The notes")
            .Build();
        if (source is { } declared)
            task.SetDeliverable(new TaskDeliverable { Path = NotesPath, Source = declared });
        return task;
    }

    private static SimpleExecutionContext Context() => new(
        CrewId.From(Guid.NewGuid()),
        new Dictionary<string, string>(),
        NullMemoryScope.Instance,
        [],
        CancellationToken.None);

    private ExecutionOrchestrator Orchestrator(
        IFileSystemService fileSystem,
        ILogger<ExecutionOrchestrator>? logger = null,
        IDeliverableResolverFactory? resolvers = null) =>
        new(
            logger ?? NullLogger<ExecutionOrchestrator>.Instance,
            new ScriptedBasicLlmProvider(),
            _chatClient,
            Array.Empty<IBaseTool>(),
            new StubOutputValidationPipeline(),
            new StubOutputParserFactory(),
            fullProvider: null,
            toolCallingStrategy: null,
            deliverableResolverFactory: resolvers,
            fileSystem);

    private static FakeFileSystemService Output() => new FakeFileSystemService().AddMount("/output", FileAccessRights.ReadWrite);

    private static Orkeon.Domain.Agent.Agent Agent() => new AgentBuilder().Role("Reader").Goal("Read").Build();

    [Fact]
    [Trait("BR", "CH-392")]
    public async System.Threading.Tasks.Task ShouldMarkTheDeliverableMissing_WhenTheToolCallFileDoesNotExist()
    {
        var lines = new ConcurrentQueue<CategoryLogLine>();
        var logger = new RecordingCategoryLogger("orchestrator", lines);

        var result = await Orchestrator(Output(), logger)
            .ExecuteTaskCoreAsync(Agent(), Task(DeliverableSource.ToolCall), Context(), TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.True(result.DeliverableMissing);
        Assert.Null(result.DeliverablePath);
        var warning = Assert.Single(lines, line => line.Level == LogLevel.Warning);
        Assert.Contains(NotesPath, warning.Message, StringComparison.Ordinal);
        Assert.Contains("Reader", warning.Message, StringComparison.Ordinal);
        Assert.Contains("Task 'Take notes on the lecture'", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-392")]
    public async System.Threading.Tasks.Task ShouldNameTheTaskByItsKey_WhenTheDeliverableIsMissing()
    {
        var lines = new ConcurrentQueue<CategoryLogLine>();
        var task = new CrewTaskBuilder()
            .WithKey("notes")
            .Description("Take notes on the lecture")
            .ExpectedOutput("The notes")
            .Build();
        task.SetDeliverable(new TaskDeliverable { Path = NotesPath, Source = DeliverableSource.ToolCall });

        await Orchestrator(Output(), new RecordingCategoryLogger("orchestrator", lines))
            .ExecuteTaskCoreAsync(Agent(), task, Context(), TestContext.Current.CancellationToken);

        var warning = Assert.Single(lines, line => line.Level == LogLevel.Warning);
        Assert.Contains("Task 'notes'", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(task.Id.ToString(), warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-392")]
    public async System.Threading.Tasks.Task ShouldNameTheDeliverable_WhenTheAgentWritesTheFileDuringTheTask()
    {
        var fileSystem = Output();
        _chatClient.OnAnswer = () => fileSystem.AddFile(NotesPath, "notes");

        var result = await Orchestrator(fileSystem)
            .ExecuteTaskCoreAsync(Agent(), Task(DeliverableSource.ToolCall), Context(), TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.False(result.DeliverableMissing);
        Assert.Equal(NotesPath, result.DeliverablePath);
    }

    [Fact]
    [Trait("BR", "CH-392")]
    public async System.Threading.Tasks.Task ShouldNameTheDeliverable_WhenTheAgentRewritesAFileThatExistedBefore()
    {
        var fileSystem = Output().AddFile(NotesPath, "old notes", DateTimeOffset.UtcNow.AddDays(-1));
        _chatClient.OnAnswer = () => fileSystem.AddFile(NotesPath, "new notes");

        var result = await Orchestrator(fileSystem)
            .ExecuteTaskCoreAsync(Agent(), Task(DeliverableSource.ToolCall), Context(), TestContext.Current.CancellationToken);

        Assert.False(result.DeliverableMissing);
        Assert.Equal(NotesPath, result.DeliverablePath);
    }

    [Fact]
    [Trait("BR", "CH-392")]
    public async System.Threading.Tasks.Task ShouldMarkTheDeliverableMissing_WhenTheFileWasLeftUnchangedFromBeforeTheTask()
    {
        // A file an earlier run (or an earlier attempt) left: the task did not write it.
        var fileSystem = Output().AddFile(NotesPath, "stale notes", DateTimeOffset.UtcNow.AddDays(-1));

        var result = await Orchestrator(fileSystem)
            .ExecuteTaskCoreAsync(Agent(), Task(DeliverableSource.ToolCall), Context(), TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.True(result.DeliverableMissing);
        Assert.Null(result.DeliverablePath);
    }

    // VX-2: the state before the task is taken once per task and run, at its first attempt: a retry or a
    // revision that keeps the file an earlier attempt of the same task wrote is not flagged.
    [Fact]
    [Trait("BR", "CH-392")]
    public async System.Threading.Tasks.Task ShouldNameTheDeliverable_WhenALaterAttemptKeepsTheFileAnEarlierAttemptWrote()
    {
        var fileSystem = Output();
        var orchestrator = Orchestrator(fileSystem);
        var task = Task(DeliverableSource.ToolCall);
        Orkeon.Application.Interfaces.Services.TaskResult second;
        using (DeliverableBaselineScope.Begin())
        {
            _chatClient.OnAnswer = () => fileSystem.AddFile(NotesPath, "notes", DateTimeOffset.UtcNow.AddMinutes(-1));
            await orchestrator.ExecuteTaskCoreAsync(Agent(), task, Context(), TestContext.Current.CancellationToken);
            _chatClient.OnAnswer = null;

            second = await orchestrator.ExecuteTaskCoreAsync(Agent(), task, Context(), TestContext.Current.CancellationToken);
        }

        Assert.True(second.Success, second.Error);
        Assert.False(second.DeliverableMissing);
        Assert.Equal(NotesPath, second.DeliverablePath);
    }

    [Fact]
    [Trait("BR", "CH-392")]
    public async System.Threading.Tasks.Task ShouldMarkTheDeliverableMissing_WhenALaterRunKeepsTheFileAnEarlierRunWrote()
    {
        var fileSystem = Output();
        var orchestrator = Orchestrator(fileSystem);
        var task = Task(DeliverableSource.ToolCall);
        using (DeliverableBaselineScope.Begin())
        {
            _chatClient.OnAnswer = () => fileSystem.AddFile(NotesPath, "notes", DateTimeOffset.UtcNow.AddMinutes(-1));
            await orchestrator.ExecuteTaskCoreAsync(Agent(), task, Context(), TestContext.Current.CancellationToken);
            _chatClient.OnAnswer = null;
        }

        Orkeon.Application.Interfaces.Services.TaskResult rerun;
        using (DeliverableBaselineScope.Begin())
            rerun = await orchestrator.ExecuteTaskCoreAsync(Agent(), task, Context(), TestContext.Current.CancellationToken);

        Assert.True(rerun.DeliverableMissing);
        Assert.Null(rerun.DeliverablePath);
    }

    [Fact]
    [Trait("BR", "CH-392")]
    public async System.Threading.Tasks.Task ShouldReportTheCancellation_WhenTheRunIsCancelledDuringTheProbe()
    {
        using var run = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var fileSystem = new FakeCancellingFileSystemService(Output(), run);
        _chatClient.OnAnswer = () => fileSystem.Armed = true;

        var result = await Orchestrator(fileSystem)
            .ExecuteTaskCoreAsync(Agent(), Task(DeliverableSource.ToolCall), Context(), run.Token);

        Assert.Equal(1, fileSystem.Cancellations);
        Assert.False(result.Success);
        Assert.Equal(Orkeon.Application.Interfaces.Services.AgentExitReason.Cancelled, result.ExitReason);
    }

    [Theory]
    [Trait("BR", "CH-392")]
    [InlineData(null)]
    [InlineData(DeliverableSource.None)]
    [InlineData(DeliverableSource.FinalMessage)]
    public async System.Threading.Tasks.Task ShouldNotProbe_WhenTheAgentDoesNotWriteTheDeliverable(DeliverableSource? source)
    {
        var fileSystem = new ThrowingFileSystemService();

        var result = await Orchestrator(fileSystem)
            .ExecuteTaskCoreAsync(Agent(), Task(source), Context(), TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.False(result.DeliverableMissing);
        Assert.Null(result.DeliverablePath);
        Assert.Equal(0, fileSystem.CallCount);
    }

    [Fact]
    [Trait("BR", "CH-392")]
    public async System.Threading.Tasks.Task ShouldNameTheDeliverable_WhenTheResolverPersistedIt()
    {
        var resolvers = new StubDeliverableResolverFactory();

        var result = await Orchestrator(new ThrowingFileSystemService(), resolvers: resolvers)
            .ExecuteTaskCoreAsync(Agent(), Task(DeliverableSource.FinalMessage), Context(), TestContext.Current.CancellationToken);

        Assert.Equal(1, resolvers.Calls);
        Assert.False(result.DeliverableMissing);
        Assert.Equal(NotesPath, result.DeliverablePath);
    }

    [Fact]
    [Trait("BR", "CH-392")]
    public async System.Threading.Tasks.Task ShouldNotNameTheDeliverable_WhenTheResolverWroteNothing()
    {
        var resolvers = new StubDeliverableResolverFactory { Persisted = false };

        var result = await Orchestrator(new ThrowingFileSystemService(), resolvers: resolvers)
            .ExecuteTaskCoreAsync(Agent(), Task(DeliverableSource.FinalMessage), Context(), TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.False(result.DeliverableMissing);
        Assert.Null(result.DeliverablePath);
    }

    [Fact]
    [Trait("BR", "CH-392")]
    public async System.Threading.Tasks.Task ShouldKeepTheSuccess_WhenTheProbeThrows()
    {
        var fileSystem = new ThrowingFileSystemService();

        var result = await Orchestrator(fileSystem)
            .ExecuteTaskCoreAsync(Agent(), Task(DeliverableSource.ToolCall), Context(), TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.False(result.DeliverableMissing);
        Assert.Null(result.DeliverablePath);
        Assert.Equal(1, fileSystem.CallCount);
    }
}
