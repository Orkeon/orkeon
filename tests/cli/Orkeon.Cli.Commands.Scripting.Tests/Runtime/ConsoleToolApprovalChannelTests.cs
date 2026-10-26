using System.Collections.Immutable;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Cli.Commands.Scripting.Runtime;
using Orkeon.Cli.Commands.Scripting.Tests.Fixtures;

namespace Orkeon.Cli.Commands.Scripting.Tests.Runtime;

/// <summary>
/// The REPL's tool-call approval channel: a question written to the console, answered by the line the read loop
/// hands over, one question at a time.
/// </summary>
public sealed class ConsoleToolApprovalChannelTests : IDisposable
{
    private readonly ScriptedTestConsole _console = new();
    private readonly ConsoleToolApprovalChannel _channel;

    public ConsoleToolApprovalChannelTests() => _channel = new ConsoleToolApprovalChannel(() => _console);

    public void Dispose() => _channel.Dispose();

    private static ToolApprovalRequest Request(string tool = "shell_command", string command = "npm test") => new()
    {
        ToolName = tool,
        Arguments = ImmutableDictionary<string, object?>.Empty.Add("command", command),
        Mode = "default",
        Reason = $"tool '{tool}' requires approval (mode 'default').",
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void ShouldWriteTheQuestionAndItsPrompt_WhenAToolCallIsAsked()
    {
        var decision = _channel.RequestAsync(Request(), Ct);

        Assert.False(decision.IsCompleted);
        Assert.Contains("? tool 'shell_command' requires approval (mode 'default').", _console.Output, StringComparison.Ordinal);
        Assert.Contains("shell_command {\"command\":\"npm test\"}", _console.Output, StringComparison.Ordinal);
        Assert.EndsWith("approve shell_command? [y] once / [s] session / [n] no > ", _console.Output, StringComparison.Ordinal);
        Assert.Equal("approve shell_command? [y] once / [s] session / [n] no > ", _channel.PromptOverride);
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public void ShouldNameTheTaskThatAsks_WhenABackgroundTaskRaisesTheQuestion()
    {
        using var channel = new ConsoleToolApprovalChannel(() => _console, () => ("t1", "dev-batches"));

        _ = channel.RequestAsync(Request(), Ct);

        Assert.Contains("? [t1] dev-batches: tool 'shell_command' requires approval", _console.Output, StringComparison.Ordinal);
        Assert.Equal("approve shell_command for [t1]? [y] once / [s] session / [n] no > ", channel.PromptOverride);
        var pending = Assert.Single(channel.Pending);
        Assert.Equal(new Orkeon.Cli.Abstractions.Runners.PendingToolApproval("shell_command", "{\"command\":\"npm test\"}", "t1", "dev-batches"), pending);
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public async Task ShouldNameTheAmbientTask_WhenTheQuestionComesFromDetachedWork()
    {
        var instance = new Orkeon.Cli.Commands.Scripting.Dispatch.CommandInstanceRegistry().Register(
            "dev-batches", Orkeon.Cli.Commands.Scripting.Dispatch.CommandInstanceKind.Async, "crew:dev-batches", "runs", Guid.NewGuid());

        await Task.Run(() =>
        {
            Orkeon.Cli.Commands.Scripting.Progress.ProgressAmbient.CurrentInstance = instance;
            _ = new Orkeon.Application.Services.Security.SerialisingToolApprovalChannel(_channel).RequestAsync(Request(), Ct);
        }, Ct);

        Assert.Equal(instance.Ticket, Assert.Single(_channel.Pending).Ticket);
        Assert.Contains($"? [{instance.Ticket}] dev-batches: ", _console.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public void ShouldAskTheWholeQuestionAgain_WhenTheLineIsNoAnswer()
    {
        using var channel = new ConsoleToolApprovalChannel(() => _console, () => ("t1", "dev-batches"));
        _ = channel.RequestAsync(Request(), Ct);
        var asked = _console.Output.Length;

        Assert.True(channel.TryConsume("what is this?"));

        var again = _console.Output[asked..];
        Assert.Contains("? [t1] dev-batches: tool 'shell_command' requires approval", again, StringComparison.Ordinal);
        Assert.Contains("shell_command {\"command\":\"npm test\"}", again, StringComparison.Ordinal);
        Assert.Single(channel.Pending);
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public void ShouldListNothingPending_WhenTheQuestionIsAnswered()
    {
        _ = _channel.RequestAsync(Request(), Ct);
        _channel.TryConsume("n");

        Assert.Empty(_channel.Pending);
    }

    [Theory]
    [InlineData("y", ToolApprovalDecision.AllowOnce, "shell_command allowed once")]
    [InlineData(" Yes ", ToolApprovalDecision.AllowOnce, "shell_command allowed once")]
    [InlineData("oui", ToolApprovalDecision.AllowOnce, "shell_command allowed once")]
    [InlineData("s", ToolApprovalDecision.AllowForSession, "shell_command allowed for this session")]
    [InlineData("n", ToolApprovalDecision.Deny, "shell_command denied")]
    [InlineData("NON", ToolApprovalDecision.Deny, "shell_command denied")]
    public async Task ShouldReturnTheOperatorsDecision_WhenTheNextLineAnswers(string line, ToolApprovalDecision expected, string said)
    {
        var decision = _channel.RequestAsync(Request(), Ct);

        Assert.True(_channel.TryConsume(line));

        Assert.Equal(expected, await decision);
        Assert.Null(_channel.PromptOverride);
        Assert.Contains(said, _console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldAllowTheToolWithoutAsking_WhenAllowedForTheSession()
    {
        var first = _channel.RequestAsync(Request(), Ct);
        _channel.TryConsume("s");
        await first;
        var written = _console.Output;

        var again = _channel.RequestAsync(Request(command: "npm run build"), Ct);
        var other = _channel.RequestAsync(Request(tool: "file_write"), Ct);

        Assert.Equal(ToolApprovalDecision.AllowForSession, await again);
        Assert.False(other.IsCompleted);
        Assert.DoesNotContain("npm run build", _console.Output[written.Length..], StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldLeaveTheLineToTheLoop_WhenItIsACommand()
    {
        var decision = _channel.RequestAsync(Request(), Ct);

        Assert.False(_channel.TryConsume("/cancel --ticket=t1"));

        Assert.False(decision.IsCompleted);
        Assert.NotNull(_channel.PromptOverride);
    }

    [Theory]
    [InlineData("please run it")]
    [InlineData("")]
    public void ShouldAskAgain_WhenTheLineIsNotAnAnswer(string line)
    {
        var decision = _channel.RequestAsync(Request(), Ct);

        Assert.True(_channel.TryConsume(line));

        Assert.False(decision.IsCompleted);
        Assert.Contains("answer y (allow once), s (allow for this session) or n (deny)", _console.Output, StringComparison.Ordinal);
        Assert.NotNull(_channel.PromptOverride);
    }

    [Fact]
    public void ShouldTakeNoLine_WhenNoQuestionWaits()
    {
        Assert.False(_channel.TryConsume("y"));
        Assert.Null(_channel.PromptOverride);
    }

    [Fact]
    public async Task ShouldAskTheNextQuestion_WhenTheFirstIsAnswered()
    {
        var first = _channel.RequestAsync(Request(), Ct);
        var second = _channel.RequestAsync(Request(tool: "file_write", command: "x"), Ct);
        Assert.DoesNotContain("file_write", _console.Output, StringComparison.Ordinal);

        _channel.TryConsume("n");

        Assert.Equal(ToolApprovalDecision.Deny, await first);
        Assert.False(second.IsCompleted);
        Assert.Contains("? tool 'file_write' requires approval", _console.Output, StringComparison.Ordinal);
        Assert.Equal("approve file_write? [y] once / [s] session / [n] no > ", _channel.PromptOverride);
    }

    [Fact]
    public async Task ShouldAnswerTheWaitingQuestionsAboutTheSameTool_WhenAllowedForTheSession()
    {
        var first = _channel.RequestAsync(Request(), Ct);
        var other = _channel.RequestAsync(Request(tool: "file_write"), Ct);
        var same = _channel.RequestAsync(Request(command: "npm run lint"), Ct);

        _channel.TryConsume("s");

        Assert.Equal(ToolApprovalDecision.AllowForSession, await first);
        Assert.Equal(ToolApprovalDecision.AllowForSession, await same);
        Assert.False(other.IsCompleted);
        Assert.Equal("approve file_write? [y] once / [s] session / [n] no > ", _channel.PromptOverride);
    }

    [Fact]
    public async Task ShouldWithdrawTheQuestionAndAskTheNext_WhenTheRunIsCancelled()
    {
        using var run = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var cancelled = _channel.RequestAsync(Request(), run.Token);
        var next = _channel.RequestAsync(Request(tool: "file_write"), Ct);

        await run.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Contains("approval for shell_command withdrawn", _console.Output, StringComparison.Ordinal);
        Assert.Equal("approve file_write? [y] once / [s] session / [n] no > ", _channel.PromptOverride);
        Assert.False(next.IsCompleted);
    }

    [Fact]
    public async Task ShouldDenyEveryWaitingQuestionAndLaterOnes_WhenDisposed()
    {
        var first = _channel.RequestAsync(Request(), Ct);
        var second = _channel.RequestAsync(Request(tool: "file_write"), Ct);

        _channel.Dispose();

        Assert.Equal(ToolApprovalDecision.Deny, await first);
        Assert.Equal(ToolApprovalDecision.Deny, await second);
        Assert.Equal(ToolApprovalDecision.Deny, await _channel.RequestAsync(Request(), Ct));
        Assert.Null(_channel.PromptOverride);
    }

    // ── a command holds the loop (runCrew): it reads the answer itself ──────

    private static int Occurrences(string text, string value)
        => (text.Length - text.Replace(value, "", StringComparison.Ordinal).Length) / value.Length;

    [Fact]
    public async Task ShouldReadTheAnswerFromTheConsole_WhenACommandHoldsTheLoop()
    {
        var decision = _channel.RequestAsync(Request(), Ct);
        _console.EnqueueLine("y");

        Assert.True(_channel.AnswerFromConsole());

        Assert.Equal(ToolApprovalDecision.AllowOnce, await decision);
        Assert.Equal(1, Occurrences(_console.Output, "approve shell_command?"));
        Assert.False(_channel.HasQuestion);
    }

    [Fact]
    public async Task ShouldWriteThePrompt_WhenTheNextQuestionCameInWithoutOne()
    {
        var first = _channel.RequestAsync(Request(), Ct);
        var second = _channel.RequestAsync(Request(tool: "file_write"), Ct);
        _console.EnqueueLine("n");
        _console.EnqueueLine("s");

        _channel.AnswerFromConsole();
        _channel.AnswerFromConsole();

        Assert.Equal(ToolApprovalDecision.Deny, await first);
        Assert.Equal(ToolApprovalDecision.AllowForSession, await second);
        Assert.Equal(1, Occurrences(_console.Output, "approve file_write?"));
    }

    [Fact]
    public async Task ShouldAskAgain_WhenTheLineReadIsACommand()
    {
        var decision = _channel.RequestAsync(Request(), Ct);
        _console.EnqueueLine("/help");
        _console.EnqueueLine("y");

        _channel.AnswerFromConsole();
        Assert.False(decision.IsCompleted);
        Assert.Contains("a command cannot run while this one waits for its crew", _console.Output, StringComparison.Ordinal);
        _channel.AnswerFromConsole();

        Assert.Equal(ToolApprovalDecision.AllowOnce, await decision);
        Assert.Equal(2, Occurrences(_console.Output, "approve shell_command?"));
    }

    [Fact]
    public async Task ShouldRefuseTheQuestion_WhenTheInputEnds()
    {
        var decision = _channel.RequestAsync(Request(), Ct);
        _console.CompleteInput();

        _channel.AnswerFromConsole();

        Assert.Equal(ToolApprovalDecision.Deny, await decision);
    }

    [Fact]
    public void ShouldReadNothing_WhenNoQuestionWaits()
    {
        Assert.False(_channel.AnswerFromConsole());
        Assert.Equal("", _console.Output);
    }

    [Fact]
    public void ShouldCutTheArguments_WhenTheyAreLong()
    {
        _ = _channel.RequestAsync(Request(command: new string('x', 1000)), Ct);

        Assert.Contains(new string('x', 250) + "…", _console.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('x', 300), _console.Output, StringComparison.Ordinal);
    }

    // -- PendingChanged: what the replay waits on --------------------------

    [Fact]
    public void ShouldRaisePendingChanged_WhenAQuestionIsAskedAndWhenItIsAnswered()
    {
        var raised = new List<int>();
        _channel.PendingChanged += (_, _) => raised.Add(_channel.Pending.Count);

        _ = _channel.RequestAsync(Request(), Ct);
        _channel.TryConsume("y");

        Assert.Equal([1, 0], raised);
    }

    [Fact]
    public async Task ShouldRaisePendingChanged_WhenAQuestionIsWithdrawn()
    {
        using var run = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var cancelled = _channel.RequestAsync(Request(), run.Token);
        var raised = 0;
        _channel.PendingChanged += (_, _) => raised++;

        await run.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Equal(1, raised);
        Assert.Empty(_channel.Pending);
    }

    [Fact]
    public void ShouldRaisePendingChanged_WhenDisposedWithQuestionsWaiting()
    {
        _ = _channel.RequestAsync(Request(), Ct);
        var raised = 0;
        _channel.PendingChanged += (_, _) => raised++;

        _channel.Dispose();

        Assert.Equal(1, raised);
    }

    [Fact]
    public void ShouldNotRaisePendingChanged_WhenDisposedWithNothingWaiting()
    {
        var raised = 0;
        _channel.PendingChanged += (_, _) => raised++;

        _channel.Dispose();

        Assert.Equal(0, raised);
    }

    [Fact]
    public void ShouldNotRaisePendingChanged_WhenTheLineIsLeftToTheLoop()
    {
        _ = _channel.RequestAsync(Request(), Ct);
        var raised = 0;
        _channel.PendingChanged += (_, _) => raised++;

        Assert.False(_channel.TryConsume("/tasks"));

        Assert.Equal(0, raised);
    }
}
