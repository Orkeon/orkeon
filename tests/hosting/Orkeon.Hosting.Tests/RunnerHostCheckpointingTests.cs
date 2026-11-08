using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Infrastructure.Checkpointing;
using ICrewRepository = Orkeon.Domain.Crew.ICrewRepository;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// FWK-01: the runner host binds <c>Orkeon:Checkpointing</c>, so a durable store is a matter of
/// configuration, and it mounts the state folder the file stores write under itself: no test here
/// hands it the mount.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerHostCheckpointingTests : IDisposable
{
    private const string SqliteNoConnectionString =
        """{ "RaggableTree": { "Enabled": false }, "Orkeon": { "Checkpointing": { "Enabled": true, "Store": "sqlite" } } }""";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-checkpointing-" + Guid.NewGuid().ToString("N"));

    public RunnerHostCheckpointingTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private string Settings(Dictionary<string, string> values)
    {
        var path = Path.Combine(_root, "appsettings.json");
        values["RaggableTree:Enabled"] = "false";
        File.WriteAllText(path, SettingsJson.Of(values));
        return path;
    }

    private string StateFolder => Path.Combine(_root, ".orkeon", "state");

    private RunnerMountPlan InRoot() => new() { WorkingDirectory = _root };

    [Fact]
    [Trait("BR", "FWK-01")]
    public async Task ShouldBackTheCheckpointManagerWithSqlite_UnderTheStateMountTheHostPlansItself()
    {
        var settings = Settings(new()
        {
            ["Orkeon:Checkpointing:Enabled"] = "true",
            ["Orkeon:Checkpointing:Store"] = "sqlite",
        });

        using var host = RunnerHost.Build(settings, InRoot());
        var store = host.Services.GetRequiredService<IStateStore>();
        var id = ExecutionId.New();
        await host.Services.GetRequiredService<ICheckpointManager>()
            .StartSessionAsync(id, "crew@/crew/crew.yaml", TestContext.Current.CancellationToken);

        Assert.IsType<SqliteStateStore>(store);
        Assert.True(File.Exists(Path.Combine(StateFolder, "checkpoints.db")));
        Assert.NotNull(await store.GetAsync(id.ToString(), TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("BR", "FWK-01")]
    public async Task ShouldWriteOneJsonFilePerSession_UnderTheStateMount_ForTheJsonStore()
    {
        var settings = Settings(new()
        {
            ["Orkeon:Checkpointing:Enabled"] = "true",
            ["Orkeon:Checkpointing:Store"] = "json",
        });

        using var host = RunnerHost.Build(settings, InRoot());
        var id = ExecutionId.New();
        await host.Services.GetRequiredService<ICheckpointManager>()
            .StartSessionAsync(id, "crew", TestContext.Current.CancellationToken);

        Assert.Contains(Directory.EnumerateFiles(StateFolder, "*", SearchOption.AllDirectories), f => f.Contains(id.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    [Trait("BR", "FWK-01")]
    public async Task ShouldReachTheDefaultSqliteFile_WhenACrewRunsThroughTheRunnerExecution()
    {
        // The real entry point of `orkeon run <crew.yaml>`, over a working directory of its own (never the
        // process's, which every pass shares): the default /state/checkpoints.db must reach the file the
        // host mounted under it, not fail on first use.
        var configPath = Path.Combine(_root, "config.yaml");
        await File.WriteAllTextAsync(Path.Combine(_root, "appsettings.json"), SqliteNoConnectionString, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(configPath, """
            name: "checkpointed"
            goal: "A crew whose run starts a checkpoint session"
            process: "sequential"
            agents:
              worker:
                role: "Worker"
                goal: "Work"
                backstory: "A minimal test agent."
                maxIter: 1
            tasks:
              do_work:
                description: "Do the work."
                expectedOutput: "The work."
                agent: "worker"
            """, TestContext.Current.CancellationToken);
        var database = Path.Combine(StateFolder, "checkpoints.db");
        Assert.False(File.Exists(database));
        var sessionId = ExecutionId.New();
        string? sessionFound = null;

        var origOut = Console.Out;
        var origErr = Console.Error;
        using var stdout = new StringWriter(new StringBuilder());
        using var stderr = new StringWriter(new StringBuilder());
        Console.SetOut(stdout);
        Console.SetError(stderr);
        int exit;
        try
        {
            exit = await RunnerExecution.RunOneShotAsync(
                new ExecutionOptions { ConfigPath = configPath, AllowExternalMounts = true, WorkingDirectory = _root },
                "Orkeon.Hosting.Tests",
                (_, services) => services.AddScoped<ICrewOrchestrationService>(sp => new StoreProbeOrchestrator(sp, sessionId, found => sessionFound = found)),
                TestContext.Current.CancellationToken);
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }

        Assert.True(exit == 0, $"exit={exit} stderr={stderr}");
        Assert.Equal(sessionId.ToString(), sessionFound);
        Assert.True(File.Exists(database), $"{database} was not created");
    }

    [Fact]
    [Trait("BR", "FWK-01")]
    public void ShouldKeepTheInMemoryStore_WhenTheSectionIsAbsent()
    {
        using var host = RunnerHost.Build(Settings(new()), InRoot());

        Assert.IsType<InMemoryStateStore>(host.Services.GetRequiredService<IStateStore>());
        Assert.False(Directory.Exists(StateFolder));
    }

    [Fact]
    [Trait("BR", "FWK-01")]
    public void ShouldWarnAtStart_WhenAStoreIsNamedButCheckpointingIsNotEnabled()
    {
        var settings = Settings(new() { ["Orkeon:Checkpointing:Store"] = "sqlite" });
        using var logs = new CapturingLoggerProvider();
        var original = Console.Error;
        using var stderr = new StringWriter();
        Console.SetError(stderr);
        try
        {
            using var host = RunnerHost.Build(settings, InRoot(), configureLogging: (_, b) =>
            {
                b.AddProvider(logs);
                b.SetMinimumLevel(LogLevel.Warning);
            });

            Assert.IsType<InMemoryStateStore>(host.Services.GetRequiredService<IStateStore>());
        }
        finally
        {
            Console.SetError(original);
        }

        var warning = Assert.Single(logs.Entries, e => e.Message.Contains("Orkeon:Checkpointing:Enabled", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("'sqlite'", warning.Message, StringComparison.Ordinal);
        Assert.Contains("Orkeon:Checkpointing:Enabled", stderr.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("json", true)]
    [InlineData("sqlite", true)]
    [InlineData("memory", false)]
    [InlineData("postgres", false)]
    [Trait("BR", "FWK-01")]
    public void ShouldPlanTheStateFolder_OnlyForTheFileStores(string store, bool planned)
    {
        var section = Section(("Enabled", "true"), ("Store", store));

        var folder = RunnerState.Plan(section, _root, []);

        Assert.Equal(planned, folder is not null);
        Assert.Equal(planned, Directory.Exists(StateFolder));
    }

    [Fact]
    [Trait("BR", "FWK-01")]
    public void ShouldPlanNoStateFolder_WhenDisabled_WhenSqliteNamesItsConnection_OrWhenTheOperatorMountsState()
    {
        Assert.Null(RunnerState.Plan(Section(("Store", "json")), _root, []));
        Assert.Null(RunnerState.Plan(Section(("Enabled", "true"), ("Store", "sqlite"), ("ConnectionString", "Data Source=/db/x.db")), _root, []));
        Assert.Null(RunnerState.Plan(Section(("Enabled", "true"), ("Store", "json")), _root, ["/state"]));
        Assert.False(Directory.Exists(StateFolder));
    }

    private static IConfigurationSection Section(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.ToDictionary(p => $"Orkeon:Checkpointing:{p.Key}", p => (string?)p.Value))
            .Build()
            .GetSection("Orkeon:Checkpointing");

    private sealed class ExecutionOptions : RunnerOptionsBase { }

    /// <summary>Starts the checkpoint session of the crew's run on the host's own manager, which opens the store.</summary>
    private sealed class StoreProbeOrchestrator(IServiceProvider scope, ExecutionId session, Action<string?> found) : ICrewOrchestrationService
    {
        public async Task<CrewOutput> KickoffAsync(Orkeon.Domain.Common.CrewId crewId, CrewInput input, CancellationToken cancellationToken = default)
        {
            _ = scope.GetRequiredService<ICrewRepository>();
            await scope.GetRequiredService<ICheckpointManager>().StartSessionAsync(session, "probe", cancellationToken);
            var saved = await scope.GetRequiredService<IStateStore>().GetAsync(session.ToString(), cancellationToken);
            found(saved?.SessionId);
            return new CrewOutput("started", [], TimeSpan.Zero, TokensUsed: null);
        }

        public Task<CrewOutput> KickoffAsync(Orkeon.Domain.Common.CrewId crewId, CrewInput input, KickoffOptions options, CancellationToken cancellationToken = default) =>
            KickoffAsync(crewId, input, cancellationToken);

        public Task<BatchOutput> KickoffForEachAsync(Orkeon.Domain.Common.CrewId crewId, IEnumerable<CrewInput> inputs, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CrewExecutionId> KickoffAsyncNoWait(Orkeon.Domain.Common.CrewId crewId, CrewInput input) =>
            throw new NotSupportedException();

        public Task<CrewExecutionStatus> GetExecutionStatusAsync(CrewExecutionId executionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<CrewExecutionEvent> KickoffStreamingAsync(Orkeon.Domain.Common.CrewId crewId, CrewInput input, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<CrewExecutionEvent> KickoffStreamingAsync(Orkeon.Domain.Common.CrewId crewId, CrewInput input, KickoffOptions options, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
