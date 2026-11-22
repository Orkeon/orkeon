using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Execution;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel;
using Orkeon.Scripting.Cli.Commands.Forge;
using Orkeon.Scripting.Cli.Tests.Doubles;
using Orkeon.Scripting.Toolchain;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using CrewAggregate = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>
/// #460, #461 - the trial of a forged <c>.ork.ts</c> crew loads it with the ports <c>orkeon run</c> gives a script:
/// System One's pick behind <c>ctx.llm.pick</c> and the pruning behind act's <c>prune</c>, both scoped, so both from
/// the trial's own scope. The bench wired neither: a crew calling <c>ctx.llm.pick</c> failed its trial, and <c>prune</c>
/// did nothing. The services validate scopes, so a port read from the root would fail the trial too.
/// </summary>
public sealed class ForgeTrialSystemOnePortsTests : IDisposable
{
    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "orkeon-forge-trial-ports-" + Guid.NewGuid().ToString("N"));

    private readonly StubSystemOnePicker _picker = new();
    private readonly FakeConversationPruner _pruner = new();

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    /// <summary>
    /// A rendered script crew whose agent, as the crew is built, picks an effort and hands it to an act that asks
    /// for prune; the load awaits both before handing the crew over.
    /// </summary>
    private ForgeSession ScriptSession()
    {
        var session = ForgeSession.Create(_workspace, "ports-trial", format: ForgeSession.FormatScript);
        var crewDirectory = Directory.CreateDirectory(Path.Combine(session.Directory, ForgeYamlRenderer.CrewDirectoryName)).FullName;
        File.WriteAllText(Path.Combine(crewDirectory, ForgeScriptRenderer.ScriptFileName), """
            let pending;
            const writer = agentBuilder().name("writer").role("Writer").goal("Write the summary")
                .onAgentStart((ctx) => {
                    pending = (async () => {
                        const pick = await ctx.llm.pick("Summarize the notes.", { effort: { fallback: "low" } });
                        await ctx.llm.act("Summarize the notes.", { effort: pick.effort.value, prune: true });
                    })();
                })
                .build();
            const write = taskBuilder().name("write").agent(writer)
                .description("Write the summary").expectedOutput("A summary").build();
            const crew = crewBuilder().name("ports-crew").goal("Summarize").withAgent(writer).withTask(write).build();
            await pending;
            globalThis.crew = crew;
            """);
        return session;
    }

    /// <summary>The engine host's services a trial reads, the two System One ports scoped, scopes validated.</summary>
    private ServiceProvider Services(ForgeSession session)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton<IFileSystemService>(new DiskBackedFileSystemService(session.Directory, "/forge"));
        services.AddSingleton<ILlmProvider>(new StubLlmProvider());
        services.AddScoped<Orkeon.Application.Crew.Routing.ISystemOnePicker>(_ => _picker);
        services.AddScoped<Orkeon.Application.Crew.Compaction.IConversationPruner>(_ => _pruner);
        services.AddScoped<ICrewFactory, OneAgentCrewFactory>();
        services.AddScoped<ICrewOrchestrationService, SucceedingOrchestrator>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Fact]
    [Trait("BR", "CH-460")]
    [Trait("BR", "CH-461")]
    public async Task ShouldPickAndPruneThroughTheTrialsScope_WhenAForgedScriptCrewCallsPickAndActWithPrune()
    {
        // Arrange
        var session = ScriptSession();
        using var services = Services(session);
        var bench = new ForgeCrewTestBench(services, transpilerFactory: () => PassThroughTranspiler.Instance);

        // Act
        var run = await bench.ExecuteAsync(session, 1, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(run.Success, run.Error);
        Assert.Equal("Writer", Assert.Single(_picker.Requests).AgentRole);
        Assert.NotEmpty(_pruner.Requests);
        Assert.All(_pruner.Requests, r => Assert.Equal("Writer", r.AgentRole));
    }

    private sealed class OneAgentCrewFactory : ICrewFactory
    {
        public Task<CrewAggregate> CreateFromConfigAsync(CrewConfiguration config, CancellationToken ct = default) =>
            Task.FromResult(new Orkeon.Domain.Crew.CrewBuilder()
                .Goal(config.Goal)
                .Sequential()
                .WithAgent(a => a.Role("Writer").Goal("Write"))
                .WithTask(t => t.Description("Write the summary").ExpectedOutput("A summary"))
                .Build());

        public Task<CrewAggregate> CreateFromFileAsync(string yamlFilePath, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<CrewAggregate> CreateFromDirectoryAsync(string directoryPath, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class SucceedingOrchestrator : ICrewOrchestrationService
    {
        public Task<CrewOutput> KickoffAsync(CrewId crewId, CrewInput input, CancellationToken cancellationToken = default)
        {
            var task = new TaskOutput("write", AgentId: null, "done", DateTime.UtcNow, Success: true, TimeSpan.Zero);
            return Task.FromResult(new CrewOutput("done", [task], TimeSpan.Zero, TokensUsed: null));
        }

        public Task<BatchOutput> KickoffForEachAsync(CrewId crewId, IEnumerable<CrewInput> inputs, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public System.Threading.Tasks.Task<CrewOutput> KickoffAsync(CrewId crewId, CrewInput input, KickoffOptions options, CancellationToken cancellationToken = default) =>
            KickoffAsync(crewId, input, cancellationToken);

        public Task<CrewExecutionId> KickoffNoWaitAsync(CrewId crewId, CrewInput input, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CrewExecutionStatus> GetExecutionStatusAsync(CrewExecutionId executionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<CrewExecutionEvent> KickoffStreamingAsync(CrewId crewId, CrewInput input, KickoffOptions options, CancellationToken cancellationToken = default) =>
            KickoffStreamingAsync(crewId, input, cancellationToken);

        public IAsyncEnumerable<CrewExecutionEvent> KickoffStreamingAsync(CrewId crewId, CrewInput input, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
