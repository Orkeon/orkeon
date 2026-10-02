using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Execution;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.LLMs.Profiles;
using Orkeon.Scripting.Cli.Commands.Forge;
using Orkeon.Scripting.Toolchain;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using CrewAggregate = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>
/// GAP-27, decision 5 — the trial of a forged <c>.ork.ts</c> crew resolves <c>llm.profile(name)</c>
/// the way <c>orkeon run</c> does: the bench loads the script with the host's LLM profiles, as the
/// forge's assistant and <c>orkeon run</c> already did. It loaded it without them, so a crew naming
/// a profile the host defines failed its trial ("Known profiles: default.") and ran once promoted.
/// A name the host does not define still fails, listing the ones it does.
/// </summary>
public sealed class ForgeTrialLlmProfileTests : IDisposable
{
    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "orkeon-forge-trial-profile-" + Guid.NewGuid().ToString("N"));

    private readonly RecordingCrewFactory _factory = new();

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    /// <summary>A rendered script crew whose one agent runs on the host profile <paramref name="profile"/>.</summary>
    private ForgeSession ScriptSession(string profile)
    {
        var session = ForgeSession.Create(_workspace, "profile-trial", format: ForgeSession.FormatScript);
        var crewDirectory = Directory.CreateDirectory(Path.Combine(session.Directory, ForgeYamlRenderer.CrewDirectoryName)).FullName;
        File.WriteAllText(Path.Combine(crewDirectory, ForgeScriptRenderer.ScriptFileName), $$"""
            const writer = agentBuilder().name("writer").role("Writer").goal("Write the summary")
                .llm(llm.profile("{{profile}}")).build();
            const write = taskBuilder().name("write").agent(writer)
                .description("Write the summary").expectedOutput("A summary").build();
            globalThis.crew = crewBuilder().name("profile-crew").goal("Summarize").withAgent(writer).withTask(write).build();
            """);
        return session;
    }

    /// <summary>The engine host's services a trial reads, with the host profile <c>fast</c>.</summary>
    private ServiceProvider Services(ForgeSession session)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton<IFileSystemService>(new DiskBackedFileSystemService(session.Directory, "/forge"));
        services.AddSingleton<ILlmProfileRegistry>(sp => new LlmProfileRegistry(
            sp, [new LlmProfileRegistration("fast", _ => new StubLlmProvider { Name = "fast-vendor" })]));
        services.AddScoped<ICrewFactory>(_ => _factory);
        services.AddScoped<ICrewOrchestrationService, SucceedingOrchestrator>();
        return services.BuildServiceProvider();
    }

    private static ForgeCrewTestBench Bench(IServiceProvider services) =>
        new(services, transpilerFactory: () => PassThroughTranspiler.Instance);

    [Fact]
    public async Task A_script_crew_naming_a_host_profile_passes_its_trial_on_that_profile()
    {
        var session = ScriptSession("fast");
        using var services = Services(session);

        var run = await Bench(services).ExecuteAsync(session, 1, TestContext.Current.CancellationToken);

        Assert.True(run.Success, run.Error);
        var agent = Assert.Single(Assert.Single(_factory.Configurations).Agents);
        Assert.Equal("fast", agent.LlmConfig!.Profile);
    }

    [Fact]
    public async Task A_profile_the_host_does_not_define_fails_the_trial_with_the_known_ones()
    {
        var session = ScriptSession("slow");
        using var services = Services(session);

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Bench(services).ExecuteAsync(session, 1, TestContext.Current.CancellationToken));

        var messages = string.Join(" | ", Chain(error).Select(e => e.Message));
        Assert.Contains("'slow'", messages, StringComparison.Ordinal);
        Assert.Contains("Known profiles: default, fast.", messages, StringComparison.Ordinal);
        Assert.Empty(_factory.Configurations);
    }

    private static IEnumerable<Exception> Chain(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
            yield return current;
    }

    /// <summary>Records the configuration each crew is created from.</summary>
    private sealed class RecordingCrewFactory : ICrewFactory
    {
        public List<CrewConfiguration> Configurations { get; } = [];

        public Task<CrewAggregate> CreateFromConfigAsync(CrewConfiguration config, CancellationToken ct = default)
        {
            Configurations.Add(config);
            return Task.FromResult(new Orkeon.Domain.Crew.CrewBuilder()
                .Goal(config.Goal)
                .Sequential()
                .WithAgent(a => a.Role("Writer").Goal("Write"))
                .WithTask(t => t.Description("Write the summary").ExpectedOutput("A summary"))
                .Build());
        }

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

        public Task<CrewExecutionId> KickoffAsyncNoWait(CrewId crewId, CrewInput input) =>
            throw new NotSupportedException();

        public Task<CrewExecutionStatus> GetExecutionStatusAsync(CrewExecutionId executionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<CrewExecutionEvent> KickoffStreamingAsync(CrewId crewId, CrewInput input, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
