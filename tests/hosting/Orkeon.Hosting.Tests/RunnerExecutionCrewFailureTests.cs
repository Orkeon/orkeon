using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// STUDIO-12 C5a — the one-shot runner's exit code follows the crew's outcome. KickoffAsync
/// never throws (its fault barrier turns every failure into an output), so the runner used
/// to return 0 for every crew failure: an agent that answered with nothing ended as
/// <c>run.finished success:true exitCode:0</c>. Now a failed crew exits 2 with the reason as
/// the last stderr line, the way a crew that fails to load does. Joins
/// <see cref="ConsoleSerialCollection"/> because it redirects the process-global console.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerExecutionCrewFailureTests : IDisposable
{
    private sealed class TestOptions : RunnerOptionsBase { }

    private readonly string _tempDir;

    public RunnerExecutionCrewFailureTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-hosting-crew-failure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        // Disabling RaggableTree keeps the on-device embedding model (ONNX) out of the host.
        File.WriteAllText(
            Path.Combine(_tempDir, "appsettings.json"),
            "{ \"RaggableTree\": { \"Enabled\": false } }");
        File.WriteAllText(Path.Combine(_tempDir, "config.yaml"), """
            name: "exit-code"
            goal: "Crew whose outcome is scripted by the test"
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
    public async Task A_crew_that_failed_exits_2_with_the_reason_as_the_last_stderr_line()
    {
        const string reason = "Task 01ABC (Worker) failed: The agent produced no final answer.";
        var (exit, stderr) = await RunAsync(new CrewOutput("partial text", [], TimeSpan.FromSeconds(1), TokensUsed: null)
        {
            Succeeded = false,
            Error = reason,
        });

        Assert.Equal(2, exit);
        var lastLine = stderr.TrimEnd().Split('\n').Last().TrimEnd('\r');
        Assert.Equal($"ERROR: {reason}", lastLine);
    }

    [Fact]
    public async Task A_crew_that_succeeded_still_exits_0()
    {
        var (exit, stderr) = await RunAsync(new CrewOutput("the answer", [], TimeSpan.FromSeconds(1), TokensUsed: null));

        Assert.Equal(0, exit);
        Assert.DoesNotContain("ERROR:", stderr, StringComparison.Ordinal);
    }

    private async Task<(int exit, string stderr)> RunAsync(CrewOutput scripted)
    {
        var opts = new TestOptions
        {
            ConfigPath = Path.Combine(_tempDir, "config.yaml"),
            AllowExternalMounts = true,
        };

        var origOut = Console.Out;
        var origErr = Console.Error;
        using var stdout = new StringWriter(new StringBuilder());
        using var stderr = new StringWriter(new StringBuilder());
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            var exit = await RunnerExecution.RunOneShotAsync(
                opts,
                "Orkeon.Hosting.Tests",
                (_, services) => services.AddSingleton<ICrewOrchestrationService>(new ScriptedOrchestrator(scripted)),
                TestContext.Current.CancellationToken);
            return (exit, stderr.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }

    /// <summary>Answers every kickoff with the scripted output — the crew never really runs.</summary>
    private sealed class ScriptedOrchestrator(CrewOutput output) : ICrewOrchestrationService
    {
        public Task<CrewOutput> KickoffAsync(CrewId crewId, CrewInput input, CancellationToken cancellationToken = default) =>
            Task.FromResult(output);

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
