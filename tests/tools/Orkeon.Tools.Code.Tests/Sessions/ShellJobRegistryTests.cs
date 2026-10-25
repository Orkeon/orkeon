using Orkeon.Domain.Execution;
using Orkeon.Tools.Code.Sessions;

namespace Orkeon.Tools.Code.Tests.Sessions;

/// <summary>
/// <see cref="ShellJobRegistry"/>: the detached jobs of one run, bounded at
/// <see cref="ShellJobRegistry.MaxLiveJobs"/>, readable until the run ends, killed with it. The
/// jobs are handles over completion sources: no process is started.
/// </summary>
public sealed class ShellJobRegistryTests
{
    private sealed class Job
    {
        private readonly TaskCompletionSource<int> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Job(string id) => Handle = new ShellJobHandle(id, _exit.Task, () => { Kills++; _exit.TrySetResult(137); });

        public ShellJobHandle Handle { get; }

        public int Kills { get; private set; }

        public void Exit(int code) => _exit.TrySetResult(code);

        public void Fault() => _exit.TrySetException(new InvalidOperationException("lost"));
    }

    [Fact]
    public async Task ShouldKeepTheJob_WhenItStarts()
    {
        await using var registry = new ShellJobRegistry();
        var job = new Job("job-1");

        var started = await registry.StartAsync(() => Task.FromResult(job.Handle), "/tmp/job-1.log");

        Assert.Same(started, registry.Find("job-1"));
        Assert.Equal("/tmp/job-1.log", started.OutputPath);
        Assert.Null(registry.Find("job-2"));
    }

    [Fact]
    public async Task ShouldRefuseAJob_WhenTheRegistryIsFull()
    {
        await using var registry = new ShellJobRegistry();
        var jobs = Enumerable.Range(0, ShellJobRegistry.MaxLiveJobs).Select(i => new Job($"job-{i}")).ToList();
        foreach (var job in jobs)
            await registry.StartAsync(() => Task.FromResult(job.Handle), "/tmp/log");

        Assert.True(registry.IsFull);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => registry.StartAsync(() => Task.FromResult(new Job("extra").Handle), "/tmp/log"));
        Assert.Contains("already running", ex.Message, StringComparison.Ordinal);

        // A job that exits frees its place.
        jobs[0].Exit(0);
        Assert.False(registry.IsFull);
    }

    [Fact]
    public async Task ShouldFreeThePlace_WhenALaunchFails()
    {
        await using var registry = new ShellJobRegistry();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => registry.StartAsync(() => Task.FromException<ShellJobHandle>(new InvalidOperationException("no shell")), "/tmp/log"));

        Assert.False(registry.IsFull);
        var job = new Job("after");
        Assert.NotNull(await registry.StartAsync(() => Task.FromResult(job.Handle), "/tmp/log"));
    }

    [Fact]
    public async Task ShouldRefuseAJob_WhenTheRunHasEnded()
    {
        var registry = new ShellJobRegistry();
        await registry.DisposeAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => registry.StartAsync(() => Task.FromResult(new Job("late").Handle), "/tmp/log"));

        Assert.Equal("The agent run has ended.", ex.Message);
    }

    [Fact]
    public async Task ShouldKillAJob_WhenTheRunEndsWhileItLaunches()
    {
        var registry = new ShellJobRegistry();
        var job = new Job("racing");
        var launched = new TaskCompletionSource<ShellJobHandle>(TaskCreationOptions.RunContinuationsAsynchronously);

        var start = registry.StartAsync(() => launched.Task, "/tmp/log");
        await registry.DisposeAsync();
        launched.SetResult(job.Handle);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => start);
        Assert.Equal("The agent run has ended.", ex.Message);
        Assert.Equal(1, job.Kills);
        Assert.Null(registry.Find("racing"));
    }

    [Fact]
    public async Task ShouldKillOnlyTheRunningJobs_WhenTheRunEnds()
    {
        var registry = new ShellJobRegistry();
        var running = new Job("running");
        var done = new Job("done");
        await registry.StartAsync(() => Task.FromResult(running.Handle), "/tmp/log");
        await registry.StartAsync(() => Task.FromResult(done.Handle), "/tmp/log");
        done.Exit(0);

        await registry.DisposeAsync();

        Assert.Equal(1, running.Kills);
        Assert.Equal(0, done.Kills);
    }

    [Fact]
    public void ShouldReportRunningExitedAndFaulted_WhenAJobIsRead()
    {
        var running = new Job("a");
        var exited = new Job("b");
        var faulted = new Job("c");
        exited.Exit(3);
        faulted.Fault();

        Assert.Equal(new ShellJobStatus(true, 0), ShellJobRegistry.Status(new ShellJob(running.Handle, "/tmp/a")));
        Assert.Equal(new ShellJobStatus(false, 3), ShellJobRegistry.Status(new ShellJob(exited.Handle, "/tmp/b")));
        Assert.Equal(new ShellJobStatus(false, -1), ShellJobRegistry.Status(new ShellJob(faulted.Handle, "/tmp/c")));
        Assert.Throws<ArgumentNullException>(() => ShellJobRegistry.Status(null!));
    }

    [Fact]
    public async Task ShouldRefuseANullLaunch()
    {
        await using var registry = new ShellJobRegistry();

        await Assert.ThrowsAsync<ArgumentNullException>(() => registry.StartAsync(null!, "/tmp/log"));
    }

    [Fact]
    public async Task ShouldGiveOneRegistryPerRun_AndNoneOutsideARun()
    {
        await Task.Run(async () =>
        {
            Assert.Null(ShellJobRegistry.Current());

            await using (AgentRunScope.Begin("agent-1"))
            {
                var first = ShellJobRegistry.Current();
                Assert.NotNull(first);
                Assert.Same(first, ShellJobRegistry.Current());
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ShouldGiveNoRegistry_WhenTheRunHasEnded()
    {
        await Task.Run(async () =>
        {
            var scope = AgentRunScope.Create("agent-1");
            await scope.EndAsync();

            using (scope.Enter())
                Assert.Null(ShellJobRegistry.Current());
        }, TestContext.Current.CancellationToken);
    }
}
