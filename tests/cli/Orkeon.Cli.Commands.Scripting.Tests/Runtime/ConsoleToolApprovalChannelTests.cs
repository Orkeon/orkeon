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

    [Fact]
    public void ShouldCutTheArguments_WhenTheyAreLong()
    {
        _ = _channel.RequestAsync(Request(command: new string('x', 1000)), Ct);

        Assert.Contains(new string('x', 250) + "…", _console.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('x', 300), _console.Output, StringComparison.Ordinal);
    }
}
