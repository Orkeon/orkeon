using Orkeon.Domain.Execution;
using Orkeon.Tools.Code.Confinement;
using Orkeon.Tools.Code.Sessions;
using Orkeon.Tools.Code.Tests.Doubles;

namespace Orkeon.Tools.Code.Tests.Sessions;

/// <summary>
/// <see cref="ShellSessionManager"/>: one session per agent run, opened at its first command, reused,
/// ended with the run; a session of its own for a command outside any run; a new session after a
/// timeout, a cancellation or a change of view.
/// </summary>
public sealed class ShellSessionManagerTests
{
    private static readonly ShellSessionView s_view = ShellSessionView.Empty;
    private static readonly ShellInvocation s_echo = new("echo", ["hello"], "/workspace", TimeSpan.FromSeconds(30));

    private readonly ShellSessionManager _manager = new();
    private readonly FakeShellSandbox _sandbox = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ShouldOpenTheSessionAtTheFirstCommandAndReuseIt_WithinOneRun()
    {
        await using var scope = AgentRunScope.Begin("agent-1");
        Assert.Empty(_sandbox.Sessions);

        await _manager.RunAsync(_sandbox, s_view, s_echo, Ct);
        await _manager.RunAsync(_sandbox, s_view, s_echo, Ct);

        var session = Assert.Single(_sandbox.Sessions);
        Assert.Equal(2, session.Invocations.Count);
        Assert.False(session.Disposed);
    }

    [Fact]
    public async Task ShouldDisposeTheSession_WhenTheRunEnds()
    {
        var scope = AgentRunScope.Begin("agent-1");
        await _manager.RunAsync(_sandbox, s_view, s_echo, Ct);

        await scope.DisposeAsync();

        Assert.True(Assert.Single(_sandbox.Sessions).Disposed);
    }

    [Fact]
    public async Task ShouldDisposeTheSession_WhenTheRunThrows()
    {
        async Task Run()
        {
            await using var scope = AgentRunScope.Begin("agent-1");
            await _manager.RunAsync(_sandbox, s_view, s_echo, Ct);
            throw new InvalidOperationException("the agent failed");
        }

        await Assert.ThrowsAsync<InvalidOperationException>(Run);

        Assert.True(Assert.Single(_sandbox.Sessions).Disposed);
    }

    [Fact]
    public async Task ShouldKillTheSessionAndOpenANewOne_WhenTheCallerCancelsACommand()
    {
        await using var scope = AgentRunScope.Begin("agent-1");
        _sandbox.Configure = session => session.Gate = session.Number == 1 ? new TaskCompletionSource() : null;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var call = _manager.RunAsync(_sandbox, s_view, s_echo, cts.Token);
        await _sandbox.Sessions[0].Started.Task.WaitAsync(Ct);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Assert.True(_sandbox.Sessions[0].Disposed);

        var next = await _manager.RunAsync(_sandbox, s_view, s_echo, Ct);

        Assert.Equal(2, _sandbox.Sessions.Count);
        Assert.False(next.SessionReset);
        Assert.False(_sandbox.Sessions[1].Disposed);
    }

    [Fact]
    public async Task ShouldDisposeTheSession_WhenTheRunIsCancelledAndEnds()
    {
        var scope = AgentRunScope.Begin("agent-1");
        _sandbox.Configure = session => session.Gate = new TaskCompletionSource();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var call = _manager.RunAsync(_sandbox, s_view, s_echo, cts.Token);
        await _sandbox.Sessions[0].Started.Task.WaitAsync(Ct);

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        await scope.DisposeAsync();

        Assert.True(Assert.Single(_sandbox.Sessions).Disposed);
    }

    [Fact]
    public async Task ShouldResetTheSession_AfterATimeout()
    {
        await using var scope = AgentRunScope.Begin("agent-1");
        _sandbox.Configure = session =>
        {
            if (session.Number == 1)
                session.Results.Enqueue(new ShellRunResult(-1, "", "", TimedOut: true));
        };

        var timedOut = await _manager.RunAsync(_sandbox, s_view, s_echo, Ct);

        Assert.True(timedOut.TimedOut);
        Assert.True(timedOut.SessionReset);
        Assert.True(_sandbox.Sessions[0].Disposed);

        var next = await _manager.RunAsync(_sandbox, s_view, s_echo, Ct);

        Assert.Equal(2, _sandbox.Sessions.Count);
        Assert.False(next.SessionReset);
        Assert.Equal("ok", next.Stdout);
    }

    [Fact]
    public async Task ShouldResetTheSession_WhenTheShellDies()
    {
        await using var scope = AgentRunScope.Begin("agent-1");
        _sandbox.Configure = session =>
            session.ThrowOnRun = session.Number == 1 ? new ShellSessionException("The shell session ended.") : null;

        await Assert.ThrowsAsync<ShellSessionException>(() => _manager.RunAsync(_sandbox, s_view, s_echo, Ct));
        await _manager.RunAsync(_sandbox, s_view, s_echo, Ct);

        Assert.Equal(2, _sandbox.Sessions.Count);
        Assert.True(_sandbox.Sessions[0].Disposed);
    }

    [Fact]
    public async Task ShouldOpenASessionPerCommandAndDisposeIt_OutsideAnyRun()
    {
        Assert.Null(AgentRunScope.Current);

        await _manager.RunAsync(_sandbox, s_view, s_echo, Ct);
        await _manager.RunAsync(_sandbox, s_view, s_echo, Ct);

        Assert.Equal(2, _sandbox.Sessions.Count);
        Assert.All(_sandbox.Sessions, session => Assert.True(session.Disposed));
    }

    [Fact]
    public async Task ShouldRunInASessionOfItsOwn_WhenTheRunHasAlreadyEnded()
    {
        await using var scope = AgentRunScope.Create("agent-1");
        await scope.EndAsync();

        using (scope.Enter())
            await _manager.RunAsync(_sandbox, s_view, s_echo, Ct);

        Assert.True(Assert.Single(_sandbox.Sessions).Disposed);
    }

    [Fact]
    public async Task ShouldGiveEachConcurrentRunItsOwnSession()
    {
        async Task Agent(string id)
        {
            await using var scope = AgentRunScope.Begin(id);
            await _manager.RunAsync(_sandbox, s_view, s_echo, Ct);
            await _manager.RunAsync(_sandbox, s_view, s_echo, Ct);
        }

        await Task.WhenAll(Task.Run(() => Agent("a"), Ct), Task.Run(() => Agent("b"), Ct));

        Assert.Equal(2, _sandbox.Sessions.Count);
        Assert.All(_sandbox.Sessions, session => Assert.Equal(2, session.Invocations.Count));
        Assert.All(_sandbox.Sessions, session => Assert.True(session.Disposed));
    }

    [Fact]
    public async Task ShouldQueueTheCommandsOfOneRun_WhenTheyArriveTogether()
    {
        await using var scope = AgentRunScope.Begin("agent-1");
        var gate = new TaskCompletionSource();
        _sandbox.Configure = session => session.Gate = gate;

        var first = _manager.RunAsync(_sandbox, s_view, s_echo, Ct);
        var second = _manager.RunAsync(_sandbox, s_view, s_echo, Ct);
        await _sandbox.Sessions[0].Started.Task.WaitAsync(Ct);
        gate.SetResult();
        await Task.WhenAll(first, second);

        var session = Assert.Single(_sandbox.Sessions);
        Assert.Equal(2, session.Invocations.Count);
        Assert.Equal(1, session.MaxConcurrency);
    }

    [Fact]
    public async Task ShouldReopenTheSessionInTheNewView_WhenTheMountsChange()
    {
        await using var scope = AgentRunScope.Begin("agent-1");
        var before = new ShellSessionView([new ConfinedPath("/srv/a", "/workspace", ConfinedAccess.ReadWrite)], [], false);
        var after = new ShellSessionView([new ConfinedPath("/srv/b", "/workspace", ConfinedAccess.ReadWrite)], [], false);

        await _manager.RunAsync(_sandbox, before, s_echo, Ct);
        await _manager.RunAsync(_sandbox, before, s_echo, Ct);
        await _manager.RunAsync(_sandbox, after, s_echo, Ct);

        Assert.Equal(2, _sandbox.Sessions.Count);
        Assert.True(_sandbox.Sessions[0].Disposed);
        Assert.Equal(2, _sandbox.Sessions[0].Invocations.Count);
        Assert.Same(after, _sandbox.Views[1]);
    }

    [Fact]
    public async Task ShouldKeepOneSessionPerKindOfSandbox_WithinOneRun()
    {
        await using var scope = AgentRunScope.Begin("agent-1");
        var sameKind = new FakeShellSandbox();
        var otherKind = new FakeShellSandbox { Name = "other" };

        await _manager.RunAsync(_sandbox, s_view, s_echo, Ct);
        await _manager.RunAsync(sameKind, s_view, s_echo, Ct);
        await _manager.RunAsync(otherKind, s_view, s_echo, Ct);

        Assert.Equal(2, Assert.Single(_sandbox.Sessions).Invocations.Count);
        Assert.Empty(sameKind.Sessions);
        Assert.Single(otherKind.Sessions);
    }

    [Fact]
    public void ShouldTellViewsApart_ByTheirPathsReadOnlyPathsAndNetwork()
    {
        ConfinedPath[] paths = [new("/srv/a", "/workspace", ConfinedAccess.ReadWrite)];

        Assert.Equal(new ShellSessionView(paths, ["/opt"], false).Fingerprint, new ShellSessionView([.. paths], ["/opt"], false).Fingerprint);
        Assert.NotEqual(new ShellSessionView(paths, [], false).Fingerprint, new ShellSessionView(paths, [], true).Fingerprint);
        Assert.NotEqual(new ShellSessionView(paths, [], false).Fingerprint, new ShellSessionView(paths, ["/opt"], false).Fingerprint);
        Assert.NotEqual(
            new ShellSessionView(paths, [], false).Fingerprint,
            new ShellSessionView([new("/srv/a", "/workspace", ConfinedAccess.ReadOnly)], [], false).Fingerprint);
    }
}
