using Orkeon.Constants.FileSystem;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.DependencyInjection;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Host.Tests;

/// <summary>
/// The runner itself, against the real composition — no <c>ScriptedRunner</c>. The review
/// found the host's three most delicate behaviours (outcome mapping, slot release on a
/// throwing callback, per-run state release) covered only by a double that simulated them:
/// a test proving the double refuses without acking, not that the runner does.
/// </summary>
public sealed class CrewRunnerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"orkeon-runner-test-{Guid.NewGuid():N}");
    private readonly List<string> _log = [];
    private string DebugLog => string.Join(" | ", _log.TakeLast(6));

    private sealed class SinkProvider(List<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new SinkLogger(sink, categoryName);
        public void Dispose() { }

        private sealed class SinkLogger(List<string> sink, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (sink) { sink.Add($"{category}: {formatter(state, exception)} {exception?.Message}"); }
            }
        }
    }

    public CrewRunnerTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "crew.yaml"), """
name: runner-test-crew
goal: Run one task offline
process: "sequential"
agents:
  worker:
    role: Echoist
    goal: Echo things back
tasks:
  say:
    description: Say hello
    expected_output: A greeting
    agent: worker
""");
        File.WriteAllText(Path.Combine(_dir, "broken.yaml"), "name: [this is not\n  a crew: {{{");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>Wraps a plain service provider as the IHost the runner reads ambient services from.</summary>
    private sealed class FakeHost(ServiceProvider services) : IHost
    {
        public IServiceProvider Services => services;
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() => services.Dispose();
    }

    private (CrewRunner Runner, CrewHostRegistry Registry, FakeHost Host) Build(
        TimeSpan? runTimeout = null,
        string crewFile = "crew.yaml",
        IReadOnlyList<string>? mounts = null,
        IFileSystemScope? scope = null)
    {
        var services = new ServiceCollection();
        if (scope is not null)
            services.AddSingleton(scope);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        // The boot registry the host would have built. AddOrkeonInfrastructure deliberately
        // does not register one — a host wires its own IFileSystemService or calls
        // AddOrkeonFileSystem — so the per-run namespace has to be given something to compose
        // over. The Internal sandbox mount is the point: entering a scope REPLACES the mount
        // set, and a namespace built from the crew's folders alone would drop it.
        var sandboxDir = Path.Combine(_dir, "sandbox");
        Directory.CreateDirectory(sandboxDir);
        services.AddSingleton(_ => new FileSystemRegistry(
        [
            new FileSystemMount(_dir, "/boot", FileAccessRights.Read),
            new FileSystemMount(
                sandboxDir,
                RunnerVirtualRoots.Sandbox,
                FileAccessRights.ReadWrite,
                null,
                MountVisibility.Internal),
        ]));
        services.AddLogging(b => b.AddProvider(new SinkProvider(_log)));
        // The loader reads through the VFS — the same rule the host itself follows by
        // auto-mounting each crew's directory (HostCrewMounts). The fake carries the crews
        // at their virtual paths.
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create)
            .AddMount("/crews", FileAccessRights.Read)
            .AddFile("/crews/crew.yaml", File.ReadAllText(Path.Combine(_dir, "crew.yaml")))
            .AddFile("/crews/broken.yaml", File.ReadAllText(Path.Combine(_dir, "broken.yaml"))));

        var stub = new StubLlmProvider().RespondTo(_ => new LlmResponse { Content = "[offline] answer" });
        stub.RespondToChatWith(new LlmResponse { Content = "[offline] chat answer" });
        services.AddSingleton<ILlmProvider>(stub);
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();

        // Mirrors Program.cs: the per-run progress hook the runner binds to the conversation.
        services.AddScoped<RunProgressHook>();
        services.AddScoped<Orkeon.Application.Crew.ICrewExecutionHook>(
            sp => sp.GetRequiredService<RunProgressHook>());

        var provider = services.BuildServiceProvider();
        var host = new FakeHost(provider);

        var options = Options.Create(new OrkeonHostOptions
        {
            Crews =
            [
                new HostedCrewOptions
                {
                    Name = "support",
                    Path = $"/crews/{crewFile}",
                    Mounts = mounts ?? [],
                },
            ],
            RunTimeout = runTimeout ?? TimeSpan.FromMinutes(5),
        });

        var registry = new CrewHostRegistry(options);
        var runner = new CrewRunner(
            provider.GetRequiredService<IServiceScopeFactory>(),
            registry,
            host,
            options,
            provider.GetRequiredService<ILogger<CrewRunner>>());

        return (runner, registry, host);
    }

    [Fact]
    public async Task A_successful_run_reports_Completed_with_the_answer_and_releases_its_slot()
    {
        var (runner, registry, host) = Build();
        using var _ = host;

        var result = await runner.RunAsync("support", "hello", "test:thread-1");

        Assert.True(HostedRunOutcome.Completed == result.Outcome, $"outcome={result.Outcome} message={result.Message} :: {DebugLog}");
        Assert.True(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Empty(registry.Running);
    }

    [Fact]
    public async Task A_crew_that_cannot_load_reports_Failed_generically_and_releases_its_slot()
    {
        // The detail goes to the log, not to the chat: a load failure carries absolute
        // server paths, and the thread's membership is not the operator set.
        var (runner, registry, host) = Build(crewFile: "broken.yaml");
        using var _ = host;

        var result = await runner.RunAsync("support", "hello", "test:thread-1");

        Assert.Equal(HostedRunOutcome.Failed, result.Outcome);
        Assert.Contains("host log", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("/crews", result.Message, StringComparison.Ordinal);
        Assert.Empty(registry.Running);
    }

    [Fact]
    public async Task A_timed_out_run_says_it_timed_out_not_that_someone_stopped_it()
    {
        // One cancelled token, two stories. "The run was stopped." for a timeout sent the
        // operator hunting for a user who pressed nothing.
        //
        // The deadline used to be 1 ms against a crew that answers instantly, so the test
        // raced the timer against the work and lost about once in three full-suite runs —
        // reporting Completed, which is a true outcome for a run that finished first, not a
        // defect in the message it was asserting on.
        //
        // The runner arms the deadline BEFORE awaiting the acknowledgement, and says so: the
        // ack is a network round trip, and a rate-limited channel could otherwise hold an
        // admitted slot with no timeout at all. Waiting inside the ack therefore lands after
        // a deadline that has certainly passed — two orders of magnitude of margin, and the
        // work is never what the timer is raced against.
        var (runner, registry, host) = Build(runTimeout: TimeSpan.FromMilliseconds(1));
        using var _ = host;

        var result = await runner.RunAsync(
            "support", "hello", "test:thread-1",
            onStarted: async _ => await Task.Delay(TimeSpan.FromMilliseconds(250)));

        Assert.Equal(HostedRunOutcome.Cancelled, result.Outcome);
        Assert.Contains("timed out", result.Message, StringComparison.Ordinal);
        Assert.Empty(registry.Running);
    }

    [Fact]
    public async Task A_stopped_run_says_it_was_stopped()
    {
        var (runner, registry, host) = Build();
        using var _ = host;

        var result = await runner.RunAsync(
            "support", "hello", "test:thread-1",
            onStarted: runId =>
            {
                Assert.True(registry.RequestStop(runId));
                return Task.CompletedTask;
            });

        Assert.Equal(HostedRunOutcome.Cancelled, result.Outcome);
        Assert.Contains("stopped", result.Message, StringComparison.Ordinal);
        Assert.Empty(registry.Running);
    }

    [Fact]
    public async Task A_throwing_acknowledgement_still_releases_the_slot()
    {
        // onStarted is awaited inside the try on purpose: a throwing callback that leaked
        // the slot would walk the crew one seat closer to permanently "busy".
        var (runner, registry, host) = Build();
        using var _ = host;

        var result = await runner.RunAsync(
            "support", "hello", "test:thread-1",
            onStarted: _ => throw new InvalidOperationException("channel hiccup"));

        Assert.Equal(HostedRunOutcome.Failed, result.Outcome);
        Assert.Empty(registry.Running);
    }

    [Fact]
    public async Task Task_completions_reach_the_conversation_as_progress()
    {
        // The doc promises progress as the run advances; before RunProgressHook the runner
        // emitted exactly one line per run, and the throttling downstream was unreachable.
        var (runner, _, host) = Build();
        using var _ = host;

        var progress = new List<string>();
        var result = await runner.RunAsync("support", "hello", "test:thread-1", progress.Add);

        Assert.Equal(HostedRunOutcome.Completed, result.Outcome);
        Assert.Contains(progress, line => line.Contains("Echoist", StringComparison.Ordinal));
    }

    /// <summary>
    /// Observes what the runner entered as the ambient mount set for a run.
    /// </summary>
    private sealed class SpyFileSystemScope : IFileSystemScope
    {
        private readonly AsyncLocalFileSystemScope _inner = new();

        public List<IReadOnlyList<string>> Entered { get; } = [];

        public FileSystemRegistry? Current => _inner.Current;

        public IDisposable Enter(FileSystemRegistry registry)
        {
            Entered.Add([.. registry.GetAllMountsInternal().Select(m => m.VirtualPath)]);
            return _inner.Enter(registry);
        }
    }

    /// <summary>
    /// The sandboxing property: what a hosted crew addresses as <c>/output</c> is ITS folder,
    /// so two crews may both use the name over two different physical ones. The boot registry
    /// is a single flat set where a virtual path has to be globally unique — which is why
    /// <c>HostCrewMounts</c> otherwise renames hosted crews apart (<c>/crews</c>,
    /// <c>/crews-1</c>, …) instead of letting them share a namespace.
    /// </summary>
    [Fact]
    public async Task A_crew_granted_folders_runs_in_its_own_mount_namespace()
    {
        var granted = Path.Combine(_dir, "granted");
        Directory.CreateDirectory(granted);

        var spy = new SpyFileSystemScope();
        var (runner, _, host) = Build(
            mounts: [$"{FileSystemMount.Quote(granted)}:/output:rw"], scope: spy);
        using (host)
        {
            var result = await runner.RunAsync("support", "hello", "test");

            Assert.True(result.Succeeded, DebugLog);
        }

        var entered = Assert.Single(spy.Entered);

        // Its own namespace…
        Assert.Contains("/output", entered, StringComparer.Ordinal);

        // …carrying the infrastructure the run still needs. Entering REPLACES the mount set,
        // so a namespace built from the crew's folders alone would silently take the exchange
        // log and the sandbox down with it for the whole run.
        Assert.Contains(RunnerVirtualRoots.Sandbox, entered, StringComparer.Ordinal);
    }

    /// <summary>A crew granted nothing keeps the boot mounts — no scope is entered at all.</summary>
    [Fact]
    public async Task A_crew_granted_nothing_enters_no_mount_namespace()
    {
        var spy = new SpyFileSystemScope();
        var (runner, _, host) = Build(scope: spy);
        using (host)
        {
            await runner.RunAsync("support", "hello", "test");
        }

        Assert.Empty(spy.Entered);
    }
}
