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
/// GAP-25 — a forge trial is a run, and it loads and kicks its crew off in one scope of its
/// own, the way <c>orkeon run</c> and <c>orkeon-host</c> do. The crew factory and the
/// orchestrator are scoped (their repositories are), and the bench resolved both from the
/// engine host's root provider: a host validating scopes refused the first trial, and one that
/// did not kept every trial's crew in one repository for the life of the session. The
/// container below validates scopes, and its two doubles only answer success when the
/// orchestrator kicks off the crew the factory created in the same scope.
/// </summary>
public sealed class ForgeCrewTestBenchScopeTests : IDisposable
{
    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "orkeon-forge-bench-scope-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    [Fact]
    public async Task A_trial_loads_and_kicks_off_its_crew_in_one_scope_under_scope_validation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICrewDefinitionLoader>(new StubCrewDefinitionLoader());
        services.AddScoped<TrialScope>();
        services.AddScoped<ICrewFactory, FakeCrewFactory>();
        services.AddScoped<ICrewOrchestrationService, FakeScopedOrchestrator>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        var session = ForgeSession.Create(_workspace, "scoped-trial");
        var bench = new ForgeCrewTestBench(provider);

        var first = await bench.ExecuteAsync(session, 1, TestContext.Current.CancellationToken);
        var second = await bench.ExecuteAsync(session, 2, TestContext.Current.CancellationToken);

        Assert.True(first.Success, first.Error);
        Assert.True(second.Success, second.Error);
    }

    /// <summary>What one scope saw: the crew its factory created.</summary>
    private sealed class TrialScope
    {
        public CrewAggregate? Created { get; set; }
    }

    private sealed class StubCrewDefinitionLoader : ICrewDefinitionLoader
    {
        private static readonly CrewConfiguration Configuration = new() { Name = "trial", Goal = "Trial" };

        public Task<CrewConfiguration> LoadFromDirectoryAsync(string directoryPath, CancellationToken ct = default) =>
            Task.FromResult(Configuration);

        public Task<CrewConfiguration> LoadFromFileAsync(string filePath, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<CrewConfiguration> LoadFromStringAsync(string yamlContent, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public CrewDefinitionValidationResult Validate(CrewConfiguration config) =>
            throw new NotSupportedException();
    }

    /// <summary>Creates the crew into its own scope's <see cref="TrialScope"/>.</summary>
    private sealed class FakeCrewFactory(TrialScope scope) : ICrewFactory
    {
        public Task<CrewAggregate> CreateFromConfigAsync(CrewConfiguration config, CancellationToken ct = default)
        {
            scope.Created = new Orkeon.Domain.Crew.CrewBuilder()
                .Goal(config.Goal)
                .Sequential()
                .WithAgent(a => a.Role("Worker").Goal("Work"))
                .WithTask(t => t.Description("Do the work.").ExpectedOutput("The work."))
                .Build();
            return Task.FromResult(scope.Created);
        }

        public Task<CrewAggregate> CreateFromFileAsync(string yamlFilePath, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<CrewAggregate> CreateFromDirectoryAsync(string directoryPath, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    /// <summary>Succeeds only for the crew the factory of its own scope created.</summary>
    private sealed class FakeScopedOrchestrator(TrialScope scope) : ICrewOrchestrationService
    {
        public Task<CrewOutput> KickoffAsync(CrewId crewId, CrewInput input, CancellationToken cancellationToken = default)
        {
            if (scope.Created?.Id != crewId)
            {
                return Task.FromResult(new CrewOutput("Crew execution failed: crew not in this scope", [], TimeSpan.Zero, TokensUsed: null)
                {
                    Succeeded = false,
                    Error = "crew not in this scope",
                });
            }

            var task = new TaskOutput("work", AgentId: null, "done in this scope", DateTime.UtcNow, Success: true, TimeSpan.Zero);
            return Task.FromResult(new CrewOutput("done in this scope", [task], TimeSpan.Zero, TokensUsed: null));
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
