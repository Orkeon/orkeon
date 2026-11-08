using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Infrastructure.HumanInput;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Infrastructure.Tests.HumanInput;

/// <summary>FWK-23: the gate's question about a tool call is persisted like any question to a human, and a run with nobody to answer suspends.</summary>
public sealed class DurableToolApprovalChannelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ToolApprovalRequest Shell() => new()
    {
        ToolName = "shell_command",
        Arguments = new Dictionary<string, object?> { ["command"] = "ls" }.ToImmutableDictionary(),
        Mode = "default",
        Reason = "it runs a command",
    };

    private static DurableToolApprovalChannel Channel(DurableHumanInputFixture fx, IToolApprovalChannel? inner = null) =>
        new(inner, fx.Scope, fx.Manager, fx.Broker, Microsoft.Extensions.Options.Options.Create(fx.Options), fx.Clock, NullLogger<DurableHumanInputProvider>.Instance);

    private sealed class StubToolApprovalChannel(ToolApprovalDecision decision) : IToolApprovalChannel
    {
        public int Calls { get; private set; }

        public System.Threading.Tasks.Task<ToolApprovalDecision> RequestAsync(ToolApprovalRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return System.Threading.Tasks.Task.FromResult(decision);
        }
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldPersistTheApprovalAndSuspend_WhenNobodyCanAnswer()
    {
        using var fx = new DurableHumanInputFixture(HumanInputPause.Suspend);
        fx.EnterRun();

        var thrown = await Assert.ThrowsAsync<RunSuspendedException>(() => Channel(fx).RequestAsync(Shell(), Ct));

        Assert.Equal("ask#1.1", thrown.RequestId);
        var pending = (await fx.StateAsync())!.PendingInput!;
        Assert.Equal(PendingInputKind.ToolApproval, pending.Kind);
        Assert.Equal("shell_command", pending.ToolName);
        Assert.Equal(16, pending.ArgsHash!.Length);
        Assert.Equal(["allow", "allow_for_session", "deny"], pending.Choices);
        Assert.DoesNotContain("ls", pending.ArgsHash, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("allow", ToolApprovalDecision.AllowOnce)]
    [InlineData("allow_for_session", ToolApprovalDecision.AllowForSession)]
    [InlineData("deny", ToolApprovalDecision.Deny)]
    [InlineData("whatever", ToolApprovalDecision.Deny)]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldReturnTheDecisionOfTheAnswer_WhenTheResumedRunAsksAgain(string answer, ToolApprovalDecision expected)
    {
        using var fx = new DurableHumanInputFixture(HumanInputPause.Suspend);
        fx.EnterRun();
        await Assert.ThrowsAsync<RunSuspendedException>(() => Channel(fx).RequestAsync(Shell(), Ct));
        await fx.Manager.AnswerAsync(fx.Id.ToString(), null, answer, Ct);
        fx.LeaveRun();
        fx.EnterRun();

        Assert.Equal(expected, await Channel(fx).RequestAsync(Shell(), Ct));
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldAskTheInnerChannelAndRecordItsDecision_WhenAPersonAnswers()
    {
        using var fx = new DurableHumanInputFixture(HumanInputPause.Hold);
        var inner = new StubToolApprovalChannel(ToolApprovalDecision.AllowOnce);
        fx.EnterRun();

        var decision = await Channel(fx, inner).RequestAsync(Shell(), Ct);

        Assert.Equal(ToolApprovalDecision.AllowOnce, decision);
        Assert.Equal(1, inner.Calls);
        Assert.Equal("allow", (await fx.StateAsync())!.AnsweredInputs["ask#1.1"].Answer);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldDenyAndAskNoOne_WhenNoTaskIsRunningAndThereIsNoInnerChannel()
    {
        using var fx = new DurableHumanInputFixture(HumanInputPause.Suspend);

        Assert.Equal(ToolApprovalDecision.Deny, await Channel(fx).RequestAsync(Shell(), Ct));
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldGoToTheInnerChannel_WhenNoTaskIsRunning()
    {
        using var fx = new DurableHumanInputFixture(HumanInputPause.Suspend);
        var inner = new StubToolApprovalChannel(ToolApprovalDecision.AllowForSession);

        Assert.Equal(ToolApprovalDecision.AllowForSession, await Channel(fx, inner).RequestAsync(Shell(), Ct));
        Assert.Equal(1, inner.Calls);
    }

    private static ToolApprovalRequest Shell(string command) => Shell() with
    {
        Arguments = new Dictionary<string, object?> { ["command"] = command }.ToImmutableDictionary(),
    };

    private sealed class SwitchingToolApprovalChannel : IToolApprovalChannel
    {
        public ToolApprovalDecision Decision { get; set; }

        public System.Threading.Tasks.Task<ToolApprovalDecision> RequestAsync(ToolApprovalRequest request, CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(Decision);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldAskAgain_WhenTheResumedRunCallsAnotherCommandUnderTheSameQuestionId()
    {
        using var fx = new DurableHumanInputFixture(HumanInputPause.Suspend);
        fx.EnterRun();
        await Assert.ThrowsAsync<RunSuspendedException>(() => Channel(fx).RequestAsync(Shell("ls"), Ct));
        await fx.Manager.AnswerAsync(fx.Id.ToString(), null, "allow", Ct);
        fx.LeaveRun();
        fx.EnterRun();

        // The replayed task now wants to run something else: the allow given to `ls` does not approve it.
        var asked = await Assert.ThrowsAsync<RunSuspendedException>(() => Channel(fx).RequestAsync(Shell("rm -rf /workspace"), Ct));

        Assert.Equal("ask#1.1", asked.RequestId);
        Assert.NotEqual((await fx.StateAsync())!.PendingInput!.ArgsHash, ArgumentsHashOf("ls"));
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldNotApproveTheRetryWithTheFirstAttemptsAnswer_WhenItCallsWithOtherArguments()
    {
        using var fx = new DurableHumanInputFixture(HumanInputPause.Hold);
        var inner = new SwitchingToolApprovalChannel { Decision = ToolApprovalDecision.AllowOnce };
        fx.EnterRun();
        Assert.Equal(ToolApprovalDecision.AllowOnce, await Channel(fx, inner).RequestAsync(Shell("ls"), Ct));

        // A retry replays the task: its questions are numbered from one again, the person now refuses.
        Orkeon.Infrastructure.Checkpointing.TaskInputScope.Current!.BeginAttempt();
        inner.Decision = ToolApprovalDecision.Deny;

        Assert.Equal(ToolApprovalDecision.Deny, await Channel(fx, inner).RequestAsync(Shell("rm -rf /workspace"), Ct));
        Assert.Equal(ToolApprovalDecision.Deny, await ReplayAsync());

        async System.Threading.Tasks.Task<ToolApprovalDecision> ReplayAsync()
        {
            Orkeon.Infrastructure.Checkpointing.TaskInputScope.Current!.BeginAttempt();
            return await Channel(fx, inner).RequestAsync(Shell("rm -rf /workspace"), Ct);
        }
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldReuseTheAnswer_WhenTheRetryMakesTheSameCall()
    {
        using var fx = new DurableHumanInputFixture(HumanInputPause.Hold);
        var inner = new SwitchingToolApprovalChannel { Decision = ToolApprovalDecision.AllowOnce };
        fx.EnterRun();
        await Channel(fx, inner).RequestAsync(Shell("ls"), Ct);
        Orkeon.Infrastructure.Checkpointing.TaskInputScope.Current!.BeginAttempt();
        inner.Decision = ToolApprovalDecision.Deny;

        Assert.Equal(ToolApprovalDecision.AllowOnce, await Channel(fx, inner).RequestAsync(Shell("ls"), Ct));
    }

    private static string ArgumentsHashOf(string command) =>
        ArgumentsHash.Of(new Dictionary<string, object?> { ["command"] = command });
}
