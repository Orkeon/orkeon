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
        using var manager = Manager();
        FirefoxSession first, second;

        await using (AgentRunScope.Begin("agent-1"))
        {
            first = await manager.UseAsync(FirefoxProfileChoice.Disposable, false, Same, Ct);
            await manager.UseAsync(FirefoxProfileChoice.Disposable, false, async (session, ct) =>
            {
                await session.NavigateAsync(Home, ct);
                return "done";
            }, Ct);
            second = await manager.UseAsync(FirefoxProfileChoice.Disposable, false, Same, Ct);

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
        using var manager = Manager();

        await using (AgentRunScope.Begin("agent-1"))
            Assert.False(await manager.CloseAsync(FirefoxProfileChoice.Disposable, false, Ct));

        Assert.Empty(Launcher.Browsers);
    }

    [Fact]
    public async Task Should_GiveEachRunItsOwnBrowser()
    {
        using var manager = Manager();
        var bothIn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;

        async Task<FirefoxSession> RunAsync(string agent)
        {
            await using (AgentRunScope.Begin(agent))
            {
                return await manager.UseAsync(FirefoxProfileChoice.Disposable, false, async (session, ct) =>
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
        using var manager = Manager();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstIn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondIn = false;

        await using (AgentRunScope.Begin("agent-1"))
        {
            var first = manager.UseAsync(FirefoxProfileChoice.Disposable, false, async (session, ct) =>
            {
                firstIn.TrySetResult();
                await release.Task.WaitAsync(ct);
                return "first";
            }, Ct);
            await firstIn.Task.WaitAsync(Ct);
            var second = manager.UseAsync(FirefoxProfileChoice.Disposable, false, (session, ct) =>
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
        using var manager = Manager();

        var first = await manager.UseAsync(FirefoxProfileChoice.Disposable, false, Same, Ct);
        var second = await manager.UseAsync(FirefoxProfileChoice.Disposable, false, Same, Ct);

        Assert.NotSame(first, second);
        Assert.Equal(2, Launcher.Browsers.Count);
        Assert.All(Launcher.Browsers, browser => Assert.True(browser.Disposed));
        Assert.False(await manager.CloseAsync(FirefoxProfileChoice.Disposable, false, Ct));
    }

    [Fact]
    public async Task Should_GiveACallThatOutlivesItsRunABrowserOfItsOwn()
    {
        using var manager = Manager();
        var runEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<FirefoxSession> late;

        await using (AgentRunScope.Begin("agent-1"))
        {
            // Started in the run, hence with its scope, and calling the tool once the run has ended.
            late = Task.Run(async () =>
            {
                await runEnded.Task.WaitAsync(Ct);
                return await manager.UseAsync(FirefoxProfileChoice.Disposable, false, Same, Ct);
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
        using var manager = Manager();

        await using (AgentRunScope.Begin("agent-1"))
        {
            var first = await manager.UseAsync(FirefoxProfileChoice.Disposable, false, Same, Ct);
            await Assert.ThrowsAsync<FirefoxException>(() => manager.UseAsync<string>(FirefoxProfileChoice.Disposable, false, 
                (_, _) => throw new FirefoxException("The node is no longer in the page."), Ct));
            var second = await manager.UseAsync(FirefoxProfileChoice.Disposable, false, Same, Ct);

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
        using var manager = Manager();

        await using (AgentRunScope.Begin("agent-1"))
        {
            var first = await manager.UseAsync(FirefoxProfileChoice.Disposable, false, Same, Ct);
            await Assert.ThrowsAnyAsync<Exception>(() => manager.UseAsync<string>(FirefoxProfileChoice.Disposable, false, 
                (_, _) => throw (cancelled ? new OperationCanceledException() : new InvalidOperationException("a bug")), Ct));

            Assert.False(first.IsAlive);
            Assert.True(Launcher.Browsers[0].Disposed);

            var second = await manager.UseAsync(FirefoxProfileChoice.Disposable, false, Same, Ct);

            Assert.NotSame(first, second);
            Assert.True(second.IsAlive);
            Assert.Equal(2, Launcher.Browsers.Count);
        }
    }

    [Fact]
    public async Task Should_StartAfresh_When_TheBrowserHasGone()
    {
        using var manager = Manager();

        await using (AgentRunScope.Begin("agent-1"))
        {
            var first = await manager.UseAsync(FirefoxProfileChoice.Disposable, false, Same, Ct);
            Launcher.Last.Wire.Close();
            await Polling.WaitUntilAsync(() => !first.IsAlive);

            var second = await manager.UseAsync(FirefoxProfileChoice.Disposable, false, Same, Ct);

            Assert.NotSame(first, second);
            Assert.True(second.IsAlive);
            Assert.True(Launcher.Browsers[0].Disposed);
            Assert.Equal(2, Launcher.Browsers.Count);
        }
    }

    [Fact]
    public async Task Should_StartAfresh_When_AnExpectedFailureComesFromABrowserThatHasGone()
    {
        using var manager = Manager();

        await using (AgentRunScope.Begin("agent-1"))
        {
            await Assert.ThrowsAsync<FirefoxException>(() => manager.UseAsync<string>(FirefoxProfileChoice.Disposable, false, async (session, _) =>
            {
                Launcher.Last.Wire.Close();
                await Polling.WaitUntilAsync(() => !session.IsAlive);
                throw new FirefoxException("The page could not be read.");
            }, Ct));

            Assert.True(Launcher.Browsers[0].Disposed);
            await manager.UseAsync(FirefoxProfileChoice.Disposable, false, Same, Ct);
            Assert.Equal(2, Launcher.Browsers.Count);
        }
    }

    [Fact]
    public async Task Should_CloseTheBrowserOfTheRunOnDemand_AndStartAnotherAtTheNextAction()
    {
        using var manager = Manager();

        await using (AgentRunScope.Begin("agent-1"))
        {
            var first = await manager.UseAsync(FirefoxProfileChoice.Disposable, false, Same, Ct);

            Assert.True(await manager.CloseAsync(FirefoxProfileChoice.Disposable, false, Ct));
            Assert.True(Launcher.Browsers[0].Disposed);
            Assert.False(await manager.CloseAsync(FirefoxProfileChoice.Disposable, false, Ct));

            var second = await manager.UseAsync(FirefoxProfileChoice.Disposable, false, Same, Ct);

            Assert.NotSame(first, second);
            Assert.Equal(2, Launcher.Browsers.Count);
        }
    }

    [Fact]
    public async Task Should_LeaveTheRunAbleToStartABrowser_When_AStartFailed()
    {
        using var manager = Manager();
        Launcher.LaunchFailure = new FirefoxException("Firefox was not found on this machine.");

        await using (AgentRunScope.Begin("agent-1"))
        {
            var error = await Assert.ThrowsAsync<FirefoxException>(() => manager.UseAsync(FirefoxProfileChoice.Disposable, false, Same, Ct));
            Launcher.LaunchFailure = null;
            var session = await manager.UseAsync(FirefoxProfileChoice.Disposable, false, Same, Ct);

            Assert.Contains("not found", error.Message, StringComparison.Ordinal);
            Assert.True(session.IsAlive);
        }

        Assert.Single(Launcher.Browsers);
    }

    [Fact]
    public async Task Should_StartTheBrowserOnTheProfileAsked_AndRefuseAnotherOneUntilClose()
    {
        using var manager = Manager();
        var work = new FirefoxProfileChoice("work");
        await using var run = AgentRunScope.Begin("agent-1");

        var first = await manager.UseAsync(work, true, Same, Ct);
        var unnamed = await manager.UseAsync(FirefoxProfileChoice.Disposable, false, Same, Ct);
        var same = await manager.UseAsync(new FirefoxProfileChoice("WORK"), true, Same, Ct);
        var error = await Assert.ThrowsAsync<FirefoxException>(() => manager.UseAsync(FirefoxProfileChoice.Disposable, true, Same, Ct));
        await manager.CloseAsync(FirefoxProfileChoice.Disposable, false, Ct);
        var next = await manager.UseAsync(FirefoxProfileChoice.Disposable, true, Same, Ct);

        Assert.Same(first, unnamed);
        Assert.Same(first, same);
        Assert.Equal("The browser of this run runs with the profile 'work': close it before opening one with 'disposable'.", error.Message);
        Assert.Equal("disposable", next.Browser.Profile.Name);
        Assert.Equal(["work", "disposable"], Launcher.Profiles);
    }

    [Fact]
    public async Task Should_ShareOneBrowserPerProfileBetweenRuns_WhichClosesWithTheLastRun()
    {
        using var manager = Manager(new FirefoxToolOptions { SharedInstance = true });
        var work = new FirefoxProfileChoice("work");
        FirefoxSession? first = null;
        var firstAttached = new TaskCompletionSource();
        var secondRunEnded = new TaskCompletionSource();

        // Two runs side by side: a run nested in another would share its resources.
        var firstRun = Task.Run(async () =>
        {
            await using var run = AgentRunScope.Begin("agent-1");
            first = await manager.UseAsync(FirefoxProfileChoice.Disposable, false, Same, Ct);
            firstAttached.SetResult();
            await secondRunEnded.Task;

            await Polling.WaitUntilAsync(() => Launcher.Browsers[0].Tabs.Count == 1 && Launcher.Browsers[1].Disposed);
            Assert.False(Launcher.Browsers[0].Disposed);
            Assert.Equal(1, manager.Pool!.BrowserCount);
            Assert.Equal("t1", first.CurrentTab);
            Assert.Equal("about:blank", (await first.ReadStateAsync(Ct)).Url);
        }, Ct);
        var secondRun = Task.Run(async () =>
        {
            await firstAttached.Task;
            await using var run = AgentRunScope.Begin("agent-2");
            var second = await manager.UseAsync(FirefoxProfileChoice.Disposable, false, Same, Ct);
            await second.NavigateAsync(Docs, Ct);
            Task<FirefoxSession> thirdRun;
            using (ExecutionContext.SuppressFlow())
            {
                thirdRun = Task.Run(async () =>
                {
                    await using var run = AgentRunScope.Begin("agent-3");
                    var third = await manager.UseAsync(work, false, Same, Ct);
                    Assert.Equal(2, manager.Pool!.BrowserCount);
                    return third;
                }, Ct);
            }

            var third = await thirdRun;

            Assert.NotSame(first, second);
            Assert.Same(first!.Browser, second.Browser);
            Assert.NotSame(first.Browser, third.Browser);
            Assert.Equal(2, Launcher.Browsers.Count);
            Assert.Equal(["tab-1", "tab-2"], Launcher.Browsers[0].Tabs);
        }, Ct);
        await secondRun;
        secondRunEnded.SetResult();
        await firstRun;

        await Polling.WaitUntilAsync(() => Launcher.Browsers[0].Disposed);
        Assert.Equal(1, Launcher.Browsers[0].Wire.Methods.Count(method => method == "browser.close"));
        Assert.Equal(0, manager.Pool!.BrowserCount);
        Assert.Equal(["disposable", "work"], Launcher.Profiles);
    }

    [Fact]
    public async Task Should_StartTheSharedBrowserAgain_When_ItDied_AndCloseItWithTheManager()
    {
        using var manager = Manager(new FirefoxToolOptions { SharedInstance = true });
        await using var run = AgentRunScope.Begin("agent-1");

        var first = await manager.UseAsync(FirefoxProfileChoice.Disposable, false, Same, Ct);
        Launcher.Browsers[0].Wire.Close();
        await Polling.WaitUntilAsync(() => !first.IsAlive);
        var second = await manager.UseAsync(FirefoxProfileChoice.Disposable, false, Same, Ct);
        manager.Dispose();

        Assert.NotSame(first.Browser, second.Browser);
        Assert.Equal(2, Launcher.Browsers.Count);
        Assert.True(Launcher.Browsers[1].Disposed);
        Assert.Equal(0, manager.Pool!.BrowserCount);
    }

    [Fact]
    public async Task Should_LeaveTheSharedBrowserAfterTheCall_When_NoRunHoldsIt()
    {
        using var manager = Manager(new FirefoxToolOptions { SharedInstance = true });

        var url = await manager.UseAsync(FirefoxProfileChoice.Disposable, false, async (session, ct) =>
        {
            await session.NavigateAsync(Home, ct);
            return (await session.ReadStateAsync(ct)).Url;
        }, Ct);

        Assert.Equal(Home, url);
        Assert.True(Assert.Single(Launcher.Browsers).Disposed);
        Assert.Equal(0, manager.Pool!.BrowserCount);
    }

    [Fact]
    public async Task Should_KeepTheHostsBrowserAcrossRuns_AndCloseItWithTheManager()
    {
        using var manager = Manager(new FirefoxToolOptions { Lifetime = FirefoxBrowserLifetime.Host });
        FirefoxSession first, second;

        await using (AgentRunScope.Begin("agent-1"))
        {
            first = await manager.UseAsync(FirefoxProfileChoice.Disposable, false, async (session, ct) =>
            {
                await session.NavigateAsync(Home, ct);
                return session;
            }, Ct);
        }

        var browser = Assert.Single(Launcher.Browsers);
        Assert.False(browser.Disposed);
        await using (AgentRunScope.Begin("agent-2"))
            second = await manager.UseAsync(FirefoxProfileChoice.Disposable, false, Same, Ct);

        Assert.Same(first, second);
        Assert.Equal(Home, (await second.ReadStateAsync(Ct)).Url);
        Assert.Equal(1, manager.HostBrowserCount);
        Assert.False(browser.Disposed);

        manager.Dispose();

        Assert.True(browser.Disposed);
        Assert.Contains("browser.close", browser.Wire.Methods);
        Assert.Equal(0, manager.HostBrowserCount);
        Assert.Single(Launcher.Browsers);
    }

    [Fact]
    public async Task Should_AttachACallOutsideAnyRunToTheHostsBrowser()
    {
        using var manager = Manager(new FirefoxToolOptions { Lifetime = FirefoxBrowserLifetime.Host });

        var first = await manager.UseAsync(FirefoxProfileChoice.Disposable, false, Same, Ct);
        var second = await manager.UseAsync(FirefoxProfileChoice.Disposable, false, Same, Ct);

        Assert.Same(first, second);
        Assert.True(second.IsAlive);
        Assert.False(Assert.Single(Launcher.Browsers).Disposed);
    }

    [Fact]
    public async Task Should_BindARunToTheProfileOfItsFirstAction_UntilClose_When_TheHostKeepsTheBrowsers()
    {
        using var manager = Manager(new FirefoxToolOptions { Lifetime = FirefoxBrowserLifetime.Host });
        var work = new FirefoxProfileChoice("work");
        await using var run = AgentRunScope.Begin("agent-1");

        var first = await manager.UseAsync(work, true, Same, Ct);
        var unnamed = await manager.UseAsync(FirefoxProfileChoice.Disposable, false, Same, Ct);
        var error = await Assert.ThrowsAsync<FirefoxException>(() => manager.UseAsync(FirefoxProfileChoice.Disposable, true, Same, Ct));
        var closeOther = await Assert.ThrowsAsync<FirefoxException>(() => manager.CloseAsync(FirefoxProfileChoice.Disposable, true, Ct));
        var closed = await manager.CloseAsync(FirefoxProfileChoice.Disposable, false, Ct);
        var next = await manager.UseAsync(FirefoxProfileChoice.Disposable, false, Same, Ct);

        Assert.Same(first, unnamed);
        Assert.Equal("The browser of this run runs with the profile 'work': close it before opening one with 'disposable'.", error.Message);
        Assert.Equal(error.Message, closeOther.Message);
        Assert.True(closed);
        Assert.True(Launcher.Browsers[0].Disposed);
        Assert.Equal("disposable", next.Browser.Profile.Name);
        Assert.Equal(["work", "disposable"], Launcher.Profiles);
    }

    [Fact]
    public async Task Should_LetARunNameAnotherProfile_When_TheHostsBrowserOfItsOwnHasGone()
    {
        using var manager = Manager(new FirefoxToolOptions { Lifetime = FirefoxBrowserLifetime.Host });
        await using var run = AgentRunScope.Begin("agent-1");

        var first = await manager.UseAsync(FirefoxProfileChoice.Disposable, false, Same, Ct);
        Launcher.Last.Wire.Close();
        await Polling.WaitUntilAsync(() => !first.IsAlive);
        var second = await manager.UseAsync(new FirefoxProfileChoice("work"), true, Same, Ct);
        manager.Dispose();

        Assert.Equal("work", second.Browser.Profile.Name);
        Assert.Equal(["disposable", "work"], Launcher.Profiles);
        Assert.All(Launcher.Browsers, browser => Assert.True(browser.Disposed));
    }

    [Fact]
    public async Task Should_CloseTheHostsBrowserOfTheProfileNamed_OutsideAnyRun()
    {
        using var manager = Manager(new FirefoxToolOptions { Lifetime = FirefoxBrowserLifetime.Host });
        var work = new FirefoxProfileChoice("work");
        await manager.UseAsync(work, true, Same, Ct);
        await manager.UseAsync(FirefoxProfileChoice.Disposable, false, Same, Ct);

        var closed = await manager.CloseAsync(work, true, Ct);
        var again = await manager.CloseAsync(work, true, Ct);

        Assert.True(closed);
        Assert.False(again);
        Assert.True(Launcher.Browsers[0].Disposed);
        Assert.False(Launcher.Browsers[1].Disposed);
        Assert.Equal(1, manager.HostBrowserCount);
    }

    [Fact]
    public async Task Should_QueueTheActionsOfOverlappingRunsOnTheHostsBrowser()
    {
        using var manager = Manager(new FirefoxToolOptions { Lifetime = FirefoxBrowserLifetime.Host });
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstIn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondIn = false;

        var first = Task.Run(async () =>
        {
            await using var run = AgentRunScope.Begin("agent-1");
            return await manager.UseAsync(FirefoxProfileChoice.Disposable, false, async (session, ct) =>
            {
                firstIn.TrySetResult();
                await release.Task.WaitAsync(ct);
                return "first";
            }, Ct);
        }, Ct);
        await firstIn.Task.WaitAsync(Ct);
        var second = Task.Run(async () =>
        {
            await using var run = AgentRunScope.Begin("agent-2");
            return await manager.UseAsync(FirefoxProfileChoice.Disposable, false, (session, ct) =>
            {
                Volatile.Write(ref secondIn, true);
                return Task.FromResult("second");
            }, Ct);
        }, Ct);
        var enteredWhileTheFirstRan = Volatile.Read(ref secondIn);
        release.TrySetResult();

        Assert.Equal(["first", "second"], await Task.WhenAll(first, second));
        Assert.False(enteredWhileTheFirstRan);
        Assert.Single(Launcher.Browsers);
    }

    [Fact]
    public async Task Should_KeepTheSharedWindowOpenForTheNextRun_When_TheHostKeepsTheBrowsers()
    {
        using var manager = Manager(new FirefoxToolOptions { SharedInstance = true, Lifetime = FirefoxBrowserLifetime.Host });
        FirefoxSession first, second;

        await using (AgentRunScope.Begin("agent-1"))
        {
            first = await manager.UseAsync(FirefoxProfileChoice.Disposable, false, async (session, ct) =>
            {
                await session.NewTabAsync(ct);
                return session;
            }, Ct);
        }

        // The run's tabs went with the run; a blank one of the browser's own keeps the window.
        var browser = Assert.Single(Launcher.Browsers);
        Assert.False(browser.Disposed);
        Assert.Equal(["tab-3"], browser.Tabs);
        Assert.Equal(1, manager.Pool!.BrowserCount);

        await using (AgentRunScope.Begin("agent-2"))
        {
            second = await manager.UseAsync(FirefoxProfileChoice.Disposable, false, (session, _) =>
            {
                Assert.Equal(["tab-3"], browser.Tabs);
                return Task.FromResult(session);
            }, Ct);
        }

        var closed = await manager.CloseAsync(FirefoxProfileChoice.Disposable, false, Ct);

        Assert.NotSame(first, second);
        Assert.Same(first.Browser, second.Browser);
        Assert.True(closed);
        Assert.True(browser.Disposed);
        Assert.Equal(0, manager.Pool!.BrowserCount);
        Assert.Single(Launcher.Browsers);
    }
}
