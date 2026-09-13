using System.Diagnostics;
using Jint;
using Jint.Native;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// <see cref="CrewRunScope"/> at the CLR level: what a host's <c>Abandon</c> releases, and how the
/// helpers behave for a continuation stranded by an abandoned drain (SCR-25 T4).
/// </summary>
public sealed class CrewRunScopeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsCrew BuildCrew(Engine engine) => (JsCrew)engine.Evaluate("""
        const a = agentBuilder().name("A").role("R").goal("G").body(() => "x").build();
        crewBuilder().name("scoped").withAgent(a).build();
        """).ToObject()!;

    /// <summary>
    /// After <c>Abandon</c> the enders are no-ops, the openers fault with the run's cancellation, what
    /// the CLR owned is released from the host thread without touching <see cref="Activity.Current"/>,
    /// and disposing afterwards throws nothing.
    /// </summary>
    [Fact]
    public async Task Abandoned_scope_helpers_are_inert()
    {
        var engine = new JsEngineFactory().Create();
        var crew = BuildCrew(engine);
        var agent = crew.findByName("A")!;
        var semaphore = crew.GetInstanceSemaphore(agent);
        var ambient = Activity.Current;

        using var scope = new CrewRunScope(crew, CrewRunKind.Run, signal: null, timeout: null, ownedByHost: true, Ct);
        scope.Start();
        await scope.AcquireAsync(agent);
        var step = scope.BeginAgent(agent);
        var attempt = scope.OpenAttempt(agent, JsValue.Undefined, 1);
        Assert.Equal(0, semaphore.CurrentCount);

        scope.Abandon();

        Assert.False(scope.IsRunning);
        Assert.True(scope.Token.IsCancellationRequested);
        Assert.Equal(1, semaphore.CurrentCount);
        Assert.Same(ambient, Activity.Current);

        scope.Release(agent);
        scope.CloseAttempt(attempt);
        scope.EndAgent(step);
        scope.Record(step, JsValue.Undefined);
        scope.End();
        Assert.Equal(1, semaphore.CurrentCount);
        Assert.Empty(scope.Finish().tasks);

        await Assert.ThrowsAsync<OperationCanceledException>(() => scope.AcquireAsync(agent));
        Assert.Throws<OperationCanceledException>(() => scope.BeginAgent(agent));
        Assert.Throws<OperationCanceledException>(() => scope.OpenAttempt(agent, JsValue.Undefined, 2));
        Assert.Throws<OperationCanceledException>(scope.ThrowIfNotRunning);
        Assert.Equal(1, semaphore.CurrentCount);
    }

    /// <summary>
    /// The loop's own end releases everything too, and a script-owned scope disposes its token sources
    /// there; a run that merely ended (not cancelled) refuses its openers with an
    /// <see cref="InvalidOperationException"/>, so a stranded chain fails loudly.
    /// </summary>
    [Fact]
    public async Task Ended_scope_refuses_its_openers_with_InvalidOperationException()
    {
        var engine = new JsEngineFactory().Create();
        var crew = BuildCrew(engine);
        var agent = crew.findByName("A")!;
        var semaphore = crew.GetInstanceSemaphore(agent);

        using var scope = new CrewRunScope(crew, CrewRunKind.Run, signal: null, timeout: null, ownedByHost: false, Ct);
        scope.Start();
        await scope.AcquireAsync(agent);

        scope.End();

        Assert.False(scope.IsRunning);
        Assert.False(scope.Token.IsCancellationRequested);
        Assert.Equal(1, semaphore.CurrentCount);
        Assert.Throws<InvalidOperationException>(scope.ThrowIfNotRunning);
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.AcquireAsync(agent));
        scope.Abandon();
        Assert.Equal(1, semaphore.CurrentCount);
    }
}
