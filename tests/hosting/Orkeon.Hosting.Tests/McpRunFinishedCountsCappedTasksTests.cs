using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Context;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Runs;
using Orkeon.Constants.Protocol;
using Orkeon.Domain.Task;
using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Infrastructure.MCP;
using Orkeon.Tests.Shared.Timing;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// CH-43 - the run.finished event of an MCP crew call carries cappedTasks, the number of tasks that ended on the iteration cap.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class McpRunFinishedCountsCappedTasksTests : IDisposable
{
    private const string History = """{ "RaggableTree": { "Enabled": false }, "Orkeon": { "RunHistory": { "Enabled": true } } }""";

    private const string TwoTasks = """
          draft:
            description: "Draft a text about {topic}."
            expectedOutput: "A draft."
            agent: "worker"
          polish:
            description: "Polish the draft, keep {topic}."
            expectedOutput: "Polished."
            agent: "worker"
            dependencies: ["draft"]
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-mcpcap-" + Guid.NewGuid().ToString("N"));
    private readonly FakeRunnerConsole _console = new();
    private readonly CappingService _service = new();

    public McpRunFinishedCountsCappedTasksTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best-effort temp cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort temp cleanup */ }
    }

    private sealed class CappingService : IAgentExecutionService
    {
        public HashSet<string> Capped { get; } = [];

        public bool Throws { get; set; }

        public System.Threading.Tasks.Task<TaskResult> ExecuteTaskAsync(
            DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
        {
            if (Throws)
                throw new InvalidOperationException("the model is unreachable");
            var key = task.Key ?? task.TaskId.ToString();
            return System.Threading.Tasks.Task.FromResult(
                new TaskResult(true, "done", null, [], TimeSpan.Zero) { EndedOnIterationCap = Capped.Contains(key) });
        }

        public System.Threading.Tasks.Task<TaskResult<TOutput>> ExecuteTaskAsync<TOutput>(
            DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
            where TOutput : class =>
            throw new NotSupportedException();
    }

    private async Task<McpCrewToolHost> OpenAsync()
    {
        var dir = Path.Combine(_root, "a");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "appsettings.json"), History, TestContext.Current.CancellationToken);
        var path = Path.Combine(dir, "crew.yaml");
        await File.WriteAllTextAsync(path, $"""
            name: "Report Writer"
            goal: "Write about a topic"
            process: "sequential"
            agents:
              worker:
                role: "Worker"
                goal: "Work"
                backstory: "A minimal test agent."
                maxIter: 1
            tasks:
            {TwoTasks}
            """, TestContext.Current.CancellationToken);
        var host = await McpCrewToolHost.OpenAsync(
            [path],
            new McpCrewHostOptions
            {
                WorkingDirectory = _root, AllowExternalMounts = true,
                Console = _console,
                ConfigureServices = (_, services) =>
                {
                    services.AddScoped<IAgentExecutionService>(_ => _service);
                    services.AddScoped<IManagerAgent>(_ => new StubAcceptingManagerAgent());
                },
            },
            TestContext.Current.CancellationToken);
        Assert.True(host is not null, _console.ErrorOutput.ToString());
        return host;
    }

    private static async Task<RunEvent> FinishedAsync(McpCrewToolHost host)
    {
        var ct = TestContext.Current.CancellationToken;
        var reader = Assert.Single(host.Hosts).Services.GetRequiredService<IRunHistoryReader>();
        var run = Assert.Single(await reader.ListAsync(new RunQuery(), ct));
        var events = await reader.ReadEventsAsync(run.RunId, 1, ct);
        return Assert.Single(events, e => e.Kind == RunEventKinds.RunFinished);
    }

    private sealed class NoProgress : IMcpProgress
    {
        public void Report(double progress, double? total = null, string? message = null) { }
    }

    [Fact]
    [Trait("BR", "CH-43")]
    public async Task ShouldCountTheCappedTasks_InTheMcpRunFinished()
    {
        _service.Capped.Add("draft");
        using var host = await OpenAsync();

        await host.CallToolAsync("run_crew_report_writer",
            JsonSerializer.SerializeToElement(new { topic = "bees" }), new NoProgress(), TestContext.Current.CancellationToken);

        var finished = await FinishedAsync(host);
        Assert.True(finished.Payload!.ContainsKey("cappedTasks"));
        Assert.Equal(1, finished.Payload["cappedTasks"]!.GetValue<int>());
    }

    [Fact]
    [Trait("BR", "CH-43")]
    public async Task ShouldWriteZeroCappedTasks_InTheMcpRunFinished_WhenNoTaskEndsOnTheCap()
    {
        using var host = await OpenAsync();

        await host.CallToolAsync("run_crew_report_writer",
            JsonSerializer.SerializeToElement(new { topic = "bees" }), new NoProgress(), TestContext.Current.CancellationToken);

        var finished = await FinishedAsync(host);
        Assert.True(finished.Payload!.ContainsKey("cappedTasks"));
        Assert.Equal(0, finished.Payload["cappedTasks"]!.GetValue<int>());
    }

    [Fact]
    [Trait("BR", "CH-43")]
    public async Task ShouldWriteZeroCappedTasks_InTheMcpRunFinished_WhenTheCallEndsBeforeAnyTask()
    {
        _service.Throws = true;
        using var host = await OpenAsync();

        await Record.ExceptionAsync(() => host.CallToolAsync("run_crew_report_writer",
            JsonSerializer.SerializeToElement(new { topic = "bees" }), new NoProgress(), TestContext.Current.CancellationToken));

        var finished = await FinishedAsync(host);
        Assert.True(finished.Payload!.ContainsKey("cappedTasks"));
        Assert.Equal(0, finished.Payload["cappedTasks"]!.GetValue<int>());
    }
}
