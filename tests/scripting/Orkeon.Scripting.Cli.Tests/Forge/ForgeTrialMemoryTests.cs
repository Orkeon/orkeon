using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Execution;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;
using Orkeon.Scripting.Cli.Commands.Forge;
using CrewAggregate = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>
/// GAP-30 — a forge trial runs without memory. Under the name of the crew being designed, a trial
/// that remembered would store its outputs, and the promoted crew would recall them as its earlier
/// runs; and a trial would need an embedder the team itself may not. The crew the trial kicks off
/// is built from the rendered configuration with its memory off, whatever the plan says.
/// </summary>
public sealed class ForgeTrialMemoryTests : IDisposable
{
    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "orkeon-forge-trial-memory-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    [Fact]
    public async Task A_trial_of_a_crew_with_memory_neither_stores_nor_recalls()
    {
        var factory = new RecordingCrewFactory();
        var services = new ServiceCollection();
        services.AddSingleton<ICrewDefinitionLoader>(new StubCrewDefinitionLoader());
        services.AddScoped<ICrewFactory>(_ => factory);
        services.AddScoped<ICrewOrchestrationService, SucceedingOrchestrator>();
        using var provider = services.BuildServiceProvider();
        var bench = new ForgeCrewTestBench(provider);

        var run = await bench.ExecuteAsync(ForgeSession.Create(_workspace, "supplier-watch"), 1, TestContext.Current.CancellationToken);

        Assert.True(run.Success, run.Error);
        var created = Assert.Single(factory.Configurations);
        Assert.False(created.Memory);
        Assert.Null(created.MemoryProvider);
        Assert.Equal("supplier-watch", created.Name);
    }

    /// <summary>The rendered crew: it remembers, in SQLite.</summary>
    private sealed class StubCrewDefinitionLoader : ICrewDefinitionLoader
    {
        private static readonly CrewConfiguration Configuration = new()
        {
            Name = "supplier-watch",
            Goal = "Summarize the supplier's offers",
            Memory = true,
            MemoryProvider = "sqlite",
        };

        public Task<CrewConfiguration> LoadFromDirectoryAsync(string directoryPath, CancellationToken ct = default) =>
            Task.FromResult(Configuration);

        public Task<CrewConfiguration> LoadFromFileAsync(string filePath, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<CrewConfiguration> LoadFromStringAsync(string yamlContent, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public CrewDefinitionValidationResult Validate(CrewConfiguration config) =>
            throw new NotSupportedException();
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
                .WithAgent(a => a.Role("Worker").Goal("Work"))
                .WithTask(t => t.Description("Do the work.").ExpectedOutput("The work."))
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
            var task = new TaskOutput("work", AgentId: null, "done", DateTime.UtcNow, Success: true, TimeSpan.Zero);
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
