using Orkeon.Domain.Execution;
using Orkeon.Tests.Shared.Timing;
using Orkeon.Tools.Web.Firefox;

namespace Orkeon.Tools.Web.Tests.Firefox;

public class FirefoxSessionManagerTests : FirefoxSessionTestBase
{
    private static Task<FirefoxSession> Same(FirefoxSession session, CancellationToken cancellationToken) => Task.FromResult(session);

    [Fact]
    public async Task Should_StartOneBrowserPerRun_AndCloseItWithTheRun()
    {
        var manager = Manager();
        FirefoxSession first, second;

        await using (AgentRunScope.Begin("agent-1"))
        {
            first = await manager.UseAsync(Same, Ct);
            await manager.UseAsync(async (session, ct) =>
            {
                await session.NavigateAsync(Home, ct);
                return "done";
            }, Ct);
            second = await manager.UseAsync(Same, Ct);

            Assert.Equal(Home, (await second.ReadStateAsync(Ct)).Url);
            Assert.False(Launcher.Last.Disposed);
        }

        Assert.Same(first, second);
        var browser = Assert.Single(Launcher.Browsers);
        Assert.True(browser.Disposed);
        Assert.Contains("browser.close", browser.Wire.Methods);
    }

    [Fact]
    public async Task Should_StartNoBrowser_When_TheRunNeverUsesOne()
    {
        var manager = Manager();

        await using (AgentRunScope.Begin("agent-1"))
            Assert.False(await manager.CloseAsync(Ct));

        Assert.Empty(Launcher.Browsers);
    }

    [Fact]
    public async Task Should_GiveEachRunItsOwnBrowser()
    {
        var manager = Manager();
        var bothIn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;

        async Task<FirefoxSession> RunAsync(string agent)
        {
            await using (AgentRunScope.Begin(agent))
            {
                return await manager.UseAsync(async (session, ct) =>
                {
                    // Both runs hold their browser at once: neither waits for the other.
                    if (Interlocked.Increment(ref entered) == 2) bothIn.TrySetResult();
                    await bothIn.Task.WaitAsync(ct);
                    return session;
                }, Ct);
            }
        }

        var sessions = await Task.WhenAll(Task.Run(() => RunAsync("agent-1"), Ct), Task.Run(() => RunAsync("agent-2"), Ct));

        Assert.NotSame(sessions[0], sessions[1]);
        Assert.Equal(2, Launcher.Browsers.Count);
        Assert.All(Launcher.Browsers, browser => Assert.True(browser.Disposed));
    }

    [Fact]
    public async Task Should_RunTheActionsOfARunOneAtATime()
    {
        var manager = Manager();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstIn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondIn = false;

        await using (AgentRunScope.Begin("agent-1"))
        {
            var first = manager.UseAsync(async (session, ct) =>
            {
                firstIn.TrySetResult();
                await release.Task.WaitAsync(ct);
                return "first";
            }, Ct);
            await firstIn.Task.WaitAsync(Ct);
            var second = manager.UseAsync((session, ct) =>
            {
                Volatile.Write(ref secondIn, true);
                return Task.FromResult("second");
            }, Ct);
            var enteredWhileTheFirstRan = Volatile.Read(ref secondIn);
            release.TrySetResult();

            Assert.Equal(["first", "second"], await Task.WhenAll(first, second));
            Assert.False(enteredWhileTheFirstRan);
        }

        Assert.Single(Launcher.Browsers);
    }

    [Fact]
    public async Task Should_GiveACallOutsideAnyRunABrowserOfItsOwn_ClosedAfterIt()
    {
        var manager = Manager();

        var first = await manager.UseAsync(Same, Ct);
        var second = await manager.UseAsync(Same, Ct);

        Assert.NotSame(first, second);
        Assert.Equal(2, Launcher.Browsers.Count);
        Assert.All(Launcher.Browsers, browser => Assert.True(browser.Disposed));
        Assert.False(await manager.CloseAsync(Ct));
    }

    [Fact]
    public async Task Should_GiveACallThatOutlivesItsRunABrowserOfItsOwn()
    {
        var manager = Manager();
        var runEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<FirefoxSession> late;

        await using (AgentRunScope.Begin("agent-1"))
        {
            // Started in the run, hence with its scope, and calling the tool once the run has ended.
            late = Task.Run(async () =>
            {
                await runEnded.Task.WaitAsync(Ct);
                return await manager.UseAsync(Same, Ct);
            }, Ct);
        }

        runEnded.TrySetResult();
        var session = await late;

        Assert.False(session.IsAlive);
        Assert.True(Assert.Single(Launcher.Browsers).Disposed);
    }

    [Fact]
    public async Task Should_KeepTheBrowser_When_AnActionFailsForAReasonTheAgentCanActOn()
    {
        var manager = Manager();

        await using (AgentRunScope.Begin("agent-1"))
        {
            var first = await manager.UseAsync(Same, Ct);
            await Assert.ThrowsAsync<FirefoxException>(() => manager.UseAsync<string>(
                (_, _) => throw new FirefoxException("The node is no longer in the page."), Ct));
            var second = await manager.UseAsync(Same, Ct);

            Assert.Same(first, second);
            Assert.True(second.IsAlive);
        }

        Assert.Single(Launcher.Browsers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_StartAfresh_When_AnActionFailsUnexpectedlyOrIsCancelled(bool cancelled)
    {
        var manager = Manager();

        await using (AgentRunScope.Begin("agent-1"))
        {
            var first = await manager.UseAsync(Same, Ct);
            await Assert.ThrowsAnyAsync<Exception>(() => manager.UseAsync<string>(
                (_, _) => throw (cancelled ? new OperationCanceledException() : new InvalidOperationException("a bug")), Ct));

            Assert.False(first.IsAlive);
            Assert.True(Launcher.Browsers[0].Disposed);

            var second = await manager.UseAsync(Same, Ct);

            Assert.NotSame(first, second);
            Assert.True(second.IsAlive);
            Assert.Equal(2, Launcher.Browsers.Count);
        }
    }

    [Fact]
    public async Task Should_StartAfresh_When_TheBrowserHasGone()
    {
        var manager = Manager();

        await using (AgentRunScope.Begin("agent-1"))
        {
            var first = await manager.UseAsync(Same, Ct);
            Launcher.Last.Wire.Close();
            await Polling.WaitUntilAsync(() => !first.IsAlive);

            var second = await manager.UseAsync(Same, Ct);

            Assert.NotSame(first, second);
            Assert.True(second.IsAlive);
            Assert.True(Launcher.Browsers[0].Disposed);
            Assert.Equal(2, Launcher.Browsers.Count);
        }
    }

    [Fact]
    public async Task Should_StartAfresh_When_AnExpectedFailureComesFromABrowserThatHasGone()
    {
        var manager = Manager();

        await using (AgentRunScope.Begin("agent-1"))
        {
            await Assert.ThrowsAsync<FirefoxException>(() => manager.UseAsync<string>(async (session, _) =>
            {
                Launcher.Last.Wire.Close();
                await Polling.WaitUntilAsync(() => !session.IsAlive);
                throw new FirefoxException("The page could not be read.");
            }, Ct));

            Assert.True(Launcher.Browsers[0].Disposed);
            await manager.UseAsync(Same, Ct);
            Assert.Equal(2, Launcher.Browsers.Count);
        }
    }

    [Fact]
    public async Task Should_CloseTheBrowserOfTheRunOnDemand_AndStartAnotherAtTheNextAction()
    {
        var manager = Manager();

        await using (AgentRunScope.Begin("agent-1"))
        {
            var first = await manager.UseAsync(Same, Ct);

            Assert.True(await manager.CloseAsync(Ct));
            Assert.True(Launcher.Browsers[0].Disposed);
            Assert.False(await manager.CloseAsync(Ct));

            var second = await manager.UseAsync(Same, Ct);

            Assert.NotSame(first, second);
            Assert.Equal(2, Launcher.Browsers.Count);
        }
    }

    [Fact]
    public async Task Should_LeaveTheRunAbleToStartABrowser_When_AStartFailed()
    {
        var manager = Manager();
        Launcher.LaunchFailure = new FirefoxException("Firefox was not found on this machine.");

        await using (AgentRunScope.Begin("agent-1"))
        {
            var error = await Assert.ThrowsAsync<FirefoxException>(() => manager.UseAsync(Same, Ct));
            Launcher.LaunchFailure = null;
            var session = await manager.UseAsync(Same, Ct);

            Assert.Contains("not found", error.Message, StringComparison.Ordinal);
            Assert.True(session.IsAlive);
        }

        Assert.Single(Launcher.Browsers);
    }
}
