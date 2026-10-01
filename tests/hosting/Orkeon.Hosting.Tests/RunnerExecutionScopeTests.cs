using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;
using ICrewRepository = Orkeon.Domain.Crew.ICrewRepository;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// GAP-25 — <c>orkeon run</c> loads and kicks its crew off inside one scope, the way
/// <c>orkeon-host</c>'s CrewRunner does per run. The crew factory and the orchestrator are
/// scoped (their repositories are), and the runner used to resolve both from the root
/// provider: a host validating scopes refused, and one that did not kept them for the life of
/// the process. The probe orchestrator below plays scope validation's part: resolved from the
/// root it throws, and it only answers success when the crew the factory loaded is in the
/// crew repository of its own scope. Joins <see cref="ConsoleSerialCollection"/> because it
/// redirects the process-global console.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerExecutionScopeTests : IDisposable
{
    private sealed class TestOptions : RunnerOptionsBase { }

    private readonly string _tempDir;

    public RunnerExecutionScopeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-hosting-scope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        // Disabling RaggableTree keeps the on-device embedding model (ONNX) out of the host.
        File.WriteAllText(Path.Combine(_tempDir, "appsettings.json"), "{ \"RaggableTree\": { \"Enabled\": false } }");
        File.WriteAllText(Path.Combine(_tempDir, "config.yaml"), """
            name: "scoped"
            goal: "Crew loaded and run in one scope"
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
            """);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* best-effort temp cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort temp cleanup */ }
    }

    [Fact]
    public async Task A_run_loads_and_kicks_off_its_crew_in_one_scope_never_from_the_root()
    {
        var opts = new TestOptions { ConfigPath = Path.Combine(_tempDir, "config.yaml"), AllowExternalMounts = true };

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
                opts,
                "Orkeon.Hosting.Tests",
                (_, services) =>
                {
                    // A singleton's factory receives the root provider: that is the reference
                    // the scoped probe compares itself against.
                    services.AddSingleton(sp => new RootProvider(sp));
                    services.AddScoped<ICrewOrchestrationService>(sp =>
                        ReferenceEquals(sp, sp.GetRequiredService<RootProvider>().Root)
                            ? throw new InvalidOperationException("ICrewOrchestrationService resolved from the root provider.")
                            : new ScopeProbeOrchestrator(sp));
                },
                TestContext.Current.CancellationToken);
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }

        Assert.True(exit == 0, $"exit={exit} stderr={stderr}");
        Assert.Contains("loaded in this scope", stdout.ToString(), StringComparison.Ordinal);
    }

    private sealed record RootProvider(IServiceProvider Root);

    /// <summary>Succeeds only when the crew it is asked to run is in its own scope's repository.</summary>
    private sealed class ScopeProbeOrchestrator(IServiceProvider scope) : ICrewOrchestrationService
    {
        public async Task<CrewOutput> KickoffAsync(CrewId crewId, CrewInput input, CancellationToken cancellationToken = default)
        {
            var crew = await scope.GetRequiredService<ICrewRepository>().GetByIdAsync(crewId, cancellationToken);
            return crew is null
                ? new CrewOutput("", [], TimeSpan.Zero, TokensUsed: null) { Succeeded = false, Error = "crew not in this scope" }
                : new CrewOutput("loaded in this scope", [], TimeSpan.Zero, TokensUsed: null);
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
