using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Runs;
using Orkeon.Constants.Protocol;
using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Infrastructure.MCP;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// FWK-24 - the crews <c>orkeon mcp serve --crew</c> exposes: one tool each, its input schema read from
/// the <c>{placeholders}</c> of the tasks, run through the one entry point <c>orkeon run</c> and
/// <c>orkeon eval</c> use, and the tasks offered as prompts.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class McpCrewToolTests : IDisposable
{
    private const string Settings = """{ "RaggableTree": { "Enabled": false } }""";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-mcpcrew-" + Guid.NewGuid().ToString("N"));
    private readonly FakeRunnerConsole _console = new();
    private readonly FakeInterpolatingAgentExecutionService _model = new();

    private IAgentExecutionService _service;

    public McpCrewToolTests()
    {
        Directory.CreateDirectory(_root);
        _service = _model;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best-effort temp cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort temp cleanup */ }
    }

    private string WriteCrew(string folder, string name, string tasks, string settings = Settings)
    {
        var dir = Path.Combine(_root, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "appsettings.json"), settings);
        var path = Path.Combine(dir, "crew.yaml");
        File.WriteAllText(path, $"""
            name: "{name}"
            goal: "Write about a topic"
            process: "sequential"
            agents:
              worker:
                role: "Worker"
                goal: "Work"
                backstory: "A minimal test agent."
                maxIter: 1
            tasks:
            {tasks}
            """);
        return path;
    }

    private const string TwoTasks = """
          draft:
            description: "Draft a text about {topic} in {tone} words."
            expectedOutput: "A draft of {length} lines."
            agent: "worker"
          polish:
            description: "Polish the draft, keep {topic}."
            expectedOutput: "Polished."
            agent: "worker"
            dependencies: ["draft"]
        """;

    private async Task<McpCrewToolHost> OpenAsync(params string[] crews)
    {
        var host = await McpCrewToolHost.OpenAsync(
            crews,
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

    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    private sealed class RecordingProgress : IMcpProgress
    {
        public List<(double Progress, double? Total, string? Message)> Reports { get; } = [];

        public void Report(double progress, double? total = null, string? message = null) => Reports.Add((progress, total, message));
    }

    [Fact]
    [Trait("BR", "FWK-24")]
    public async Task ShouldExposeOneToolPerCrew_WithASchemaFromThePlaceholders()
    {
        using var host = await OpenAsync(WriteCrew("a", "Report Writer", TwoTasks));

        var tool = Assert.Single(host.Tools);

        Assert.Equal("run_crew_report_writer", tool.Name);
        Assert.Contains("Write about a topic", tool.Description, StringComparison.Ordinal);
        var schema = tool.InputSchema!.Value;
        Assert.Equal("object", schema.GetProperty("type").GetString());
        var properties = schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["topic", "tone", "length"], properties);
        Assert.All(schema.GetProperty("properties").EnumerateObject(), p => Assert.Equal("string", p.Value.GetProperty("type").GetString()));
        Assert.Equal(["topic", "tone", "length"], schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.True(host.OwnsTool("run_crew_report_writer"));
        Assert.False(host.OwnsTool("file_read"));
    }

    [Fact]
    [Trait("BR", "FWK-24")]
    public async Task ShouldReturnTheFinalOutput_WhenTheToolIsCalledWithThePlaceholders()
    {
        using var host = await OpenAsync(WriteCrew("a", "Report Writer", TwoTasks));

        var result = await host.CallToolAsync(
            "run_crew_report_writer",
            Args(new { topic = "bees", tone = "plain", length = 3 }),
            new RecordingProgress(),
            TestContext.Current.CancellationToken);

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("Polish the draft, keep bees.", Assert.Single(result.Content).Text);
    }

    [Fact]
    [Trait("BR", "FWK-24")]
    public async Task ShouldReportOneProgressPerTask_WhenTheCrewRuns()
    {
        using var host = await OpenAsync(WriteCrew("a", "Report Writer", TwoTasks));
        var progress = new RecordingProgress();

        await host.CallToolAsync(
            "run_crew_report_writer", Args(new { topic = "bees", tone = "plain", length = "3" }), progress, TestContext.Current.CancellationToken);

        Assert.Equal(2, progress.Reports.Count);
        Assert.Equal([1d, 2d], progress.Reports.Select(r => r.Progress).ToList());
        Assert.All(progress.Reports, r => Assert.Equal(2d, r.Total));
    }

    [Fact]
    [Trait("BR", "FWK-24")]
    public async Task ShouldRefuseTheCall_WhenAPlaceholderIsMissingUnknownOrNotAScalar()
    {
        using var host = await OpenAsync(WriteCrew("a", "Report Writer", TwoTasks));
        var ct = TestContext.Current.CancellationToken;

        var missing = await Assert.ThrowsAsync<McpRequestException>(() =>
            host.CallToolAsync("run_crew_report_writer", Args(new { topic = "bees" }), new RecordingProgress(), ct));
        var unknown = await Assert.ThrowsAsync<McpRequestException>(() =>
            host.CallToolAsync("run_crew_report_writer", Args(new { topic = "a", tone = "b", length = "c", path = "/etc/passwd" }), new RecordingProgress(), ct));
        var nested = await Assert.ThrowsAsync<McpRequestException>(() =>
            host.CallToolAsync("run_crew_report_writer", Args(new { topic = new { x = 1 }, tone = "b", length = "c" }), new RecordingProgress(), ct));

        Assert.Equal(JsonRpcErrorCodes.InvalidParams, missing.Code);
        Assert.Contains("tone", missing.Message, StringComparison.Ordinal);
        Assert.Contains("length", missing.Message, StringComparison.Ordinal);
        Assert.Contains("path", unknown.Message, StringComparison.Ordinal);
        Assert.Contains("topic", nested.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-24")]
    public async Task ShouldAnswerAnErrorResult_WhenTheCrewFails()
    {
        _model.FailOn.Add("polish");
        using var host = await OpenAsync(WriteCrew("a", "Report Writer", TwoTasks));

        var result = await host.CallToolAsync(
            "run_crew_report_writer", Args(new { topic = "bees", tone = "plain", length = "3" }), new RecordingProgress(), TestContext.Current.CancellationToken);

        // The task's own error text stays in the server's log: it may carry a path or a secret of the run.
        Assert.True(result.IsError);
        var text = Assert.Single(result.Content).Text;
        Assert.DoesNotContain("polish failed", text, StringComparison.Ordinal);
        Assert.Contains("log of the server", text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-24")]
    public async Task ShouldNotReturnTheLoadError_WhenTheCrewNoLongerLoads()
    {
        var path = WriteCrew("a", "Report Writer", TwoTasks);
        using var host = await OpenAsync(path);
        const string Canary = "load-canary-7f3a";
        await File.WriteAllTextAsync(path, $"not: [a crew {Canary}", Ct);

        var result = await host.CallToolAsync("run_crew_report_writer", BeesArgs, new RecordingProgress(), Ct);

        Assert.True(result.IsError);
        var text = Assert.Single(result.Content).Text;
        Assert.DoesNotContain(Canary, text, StringComparison.Ordinal);
        Assert.Contains("log of the server", text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-24")]
    public async Task ShouldOfferEveryTaskAsAPrompt_WithItsOwnPlaceholders()
    {
        using var host = await OpenAsync(WriteCrew("a", "Report Writer", TwoTasks));

        Assert.Equal(["report_writer_task_1", "report_writer_task_2"], host.Prompts.Select(p => p.Name).ToList());
        Assert.Equal(["topic", "tone", "length"], host.Prompts[0].Arguments.Select(a => a.Name).ToList());
        Assert.All(host.Prompts[0].Arguments, a => Assert.True(a.Required));
        Assert.Equal(["topic"], host.Prompts[1].Arguments.Select(a => a.Name).ToList());
    }

    [Fact]
    [Trait("BR", "FWK-24")]
    public async Task ShouldInterpolateThePrompt_WhenGotWithItsArguments()
    {
        using var host = await OpenAsync(WriteCrew("a", "Report Writer", TwoTasks));
        var ct = TestContext.Current.CancellationToken;

        var result = await host.GetPromptAsync("report_writer_task_1", Args(new { topic = "bees", tone = "plain", length = "3" }), ct);

        var message = Assert.Single(result.Messages);
        Assert.Equal("user", message.Role);
        Assert.Contains("Draft a text about bees in plain words.", message.Content.Text, StringComparison.Ordinal);
        Assert.Contains("A draft of 3 lines.", message.Content.Text, StringComparison.Ordinal);

        var missing = await Assert.ThrowsAsync<McpRequestException>(() =>
            host.GetPromptAsync("report_writer_task_2", Args(new { }), ct));
        Assert.Contains("topic", missing.Message, StringComparison.Ordinal);
        var unknown = await Assert.ThrowsAsync<McpRequestException>(() => host.GetPromptAsync("nope", null, ct));
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, unknown.Code);
    }

    [Fact]
    [Trait("BR", "FWK-24")]
    public async Task ShouldRefuseTheStart_WhenTwoCrewsGiveTheSameToolName()
    {
        var first = WriteCrew("a", "Twin", TwoTasks);
        var second = WriteCrew("b", "Twin", TwoTasks);

        var host = await McpCrewToolHost.OpenAsync(
            [first, second],
            new McpCrewHostOptions { WorkingDirectory = _root, AllowExternalMounts = true, Console = _console },
            TestContext.Current.CancellationToken);

        Assert.Null(host);
        Assert.Contains("run_crew_twin", _console.ErrorOutput.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-24")]
    public async Task ShouldRefuseTheStart_WhenACrewDoesNotLoad()
    {
        var bad = Path.Combine(_root, "bad.yaml");
        await File.WriteAllTextAsync(bad, "not: [a crew", TestContext.Current.CancellationToken);

        var host = await McpCrewToolHost.OpenAsync(
            [bad],
            new McpCrewHostOptions { WorkingDirectory = _root, AllowExternalMounts = true, Console = _console },
            TestContext.Current.CancellationToken);

        Assert.Null(host);
        Assert.NotEmpty(_console.ErrorOutput.ToString());
    }

    [Fact]
    [Trait("BR", "FWK-24")]
    public async Task ShouldRunUnderTheGateOfOrkeonRun_WhenTheSettingsEnableIt()
    {
        const string gated = """{ "RaggableTree": { "Enabled": false }, "Orkeon": { "Security": { "PermissionGate": { "Enabled": true } } } }""";
        using var host = await OpenAsync(WriteCrew("a", "Gated", TwoTasks, gated));

        var gate = Assert.Single(host.Hosts).Services.GetService<IPermissionGate>();

        Assert.NotNull(gate);
    }
    private const string History = """{ "RaggableTree": { "Enabled": false }, "Orkeon": { "RunHistory": { "Enabled": true } } }""";

    private static readonly JsonElement BeesArgs = Args(new { topic = "bees", tone = "plain", length = "3" });

    private static JsonElement ArgsFor(string topic) => Args(new { topic, tone = "plain", length = "3" });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("BR", "FWK-24")]
    public async Task ShouldRecordTheCallInTheRunHistoryWithItsEvents_WhenTheHistoryIsOn()
    {
        using var host = await OpenAsync(WriteCrew("a", "Report Writer", TwoTasks, History));

        await host.CallToolAsync("run_crew_report_writer", BeesArgs, new RecordingProgress(), Ct);

        var reader = Assert.Single(host.Hosts).Services.GetRequiredService<IRunHistoryReader>();
        var run = Assert.Single(await reader.ListAsync(new RunQuery(), Ct));
        Assert.Equal(RunStatus.Succeeded, run.Status);
        var kinds = (await reader.ReadEventsAsync(run.RunId, 1, Ct)).Select(e => e.Kind).ToList();
        Assert.Equal(RunEventKinds.RunStarted, kinds[0]);
        Assert.Equal(RunEventKinds.RunFinished, kinds[^1]);
        Assert.True(kinds.Count > 2, string.Join(", ", kinds));
    }

    [Fact]
    [Trait("BR", "FWK-24")]
    public async Task ShouldGiveEachCallItsOwnRunInTheHistory_WhenTwoCallsRun()
    {
        using var host = await OpenAsync(WriteCrew("a", "Report Writer", TwoTasks, History));

        await host.CallToolAsync("run_crew_report_writer", BeesArgs, new RecordingProgress(), Ct);
        await host.CallToolAsync("run_crew_report_writer", ArgsFor("wasps"), new RecordingProgress(), Ct);

        var reader = Assert.Single(host.Hosts).Services.GetRequiredService<IRunHistoryReader>();
        var runs = await reader.ListAsync(new RunQuery(), Ct);
        Assert.Equal(2, runs.Select(r => r.RunId).Distinct().Count());
    }

    [Fact]
    [Trait("BR", "FWK-24")]
    public async Task ShouldRecordNothing_WhenTheHistoryIsOff()
    {
        using var host = await OpenAsync(WriteCrew("a", "Report Writer", TwoTasks));

        await host.CallToolAsync("run_crew_report_writer", BeesArgs, new RecordingProgress(), Ct);

        Assert.Null(Assert.Single(host.Hosts).Services.GetService<IRunHistoryReader>());
        Assert.False(Directory.Exists(Path.Combine(_root, ".orkeon", "runs")));
    }

    [Fact]
    [Trait("BR", "FWK-24")]
    public async Task ShouldGiveEachConcurrentCallItsOwnOutput_WhenTwoCallsOfOneCrewRunTogether()
    {
        var gated = new FakeGatedAgentExecutionService();
        _service = gated;
        gated.Release();
        using var host = await OpenAsync(WriteCrew("a", "Report Writer", TwoTasks));

        var results = await Task.WhenAll(
            host.CallToolAsync("run_crew_report_writer", ArgsFor("bees"), new RecordingProgress(), Ct),
            host.CallToolAsync("run_crew_report_writer", ArgsFor("wasps"), new RecordingProgress(), Ct));

        Assert.Equal("Polish the draft, keep bees.", Assert.Single(results[0].Content).Text);
        Assert.Equal("Polish the draft, keep wasps.", Assert.Single(results[1].Content).Text);
    }

    [Fact]
    [Trait("BR", "FWK-24")]
    public async Task ShouldRunOneCallOfACrewAtATime_WhenTwoCallsOverlap()
    {
        var gated = new FakeGatedAgentExecutionService();
        _service = gated;
        using var host = await OpenAsync(WriteCrew("a", "Report Writer", TwoTasks));

        var first = host.CallToolAsync("run_crew_report_writer", ArgsFor("bees"), new RecordingProgress(), Ct);
        await Polling.WaitUntilAsync(() => gated.Entered >= 1, HangGuard);
        var second = host.CallToolAsync("run_crew_report_writer", ArgsFor("wasps"), new RecordingProgress(), Ct);
        // The second call is queued behind the first, or (the fault) it got in beside it.
        try
        {
            await Polling.WaitUntilAsync(() => host.WaitingCalls("run_crew_report_writer") == 1 || gated.Entered >= 2, HangGuard);
        }
        catch (TimeoutException)
        {
            Assert.Fail($"second={second.Status} {second.Exception?.GetBaseException().Message} waiting={host.WaitingCalls("run_crew_report_writer")} entered={gated.Entered}");
        }

        Assert.Equal(1, gated.Entered);
        gated.Release();
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, gated.MaxInside);
        Assert.Equal("Polish the draft, keep bees.", Assert.Single(results[0].Content).Text);
        Assert.Equal("Polish the draft, keep wasps.", Assert.Single(results[1].Content).Text);
    }

    [Fact]
    [Trait("BR", "FWK-24")]
    public async Task ShouldAnswerBusy_WhenMoreCallsThanTheBoundWaitForTheCrew()
    {
        var gated = new FakeGatedAgentExecutionService();
        _service = gated;
        using var host = await OpenAsync(WriteCrew("a", "Report Writer", TwoTasks));
        var running = host.CallToolAsync("run_crew_report_writer", ArgsFor("bees"), new RecordingProgress(), Ct);
        await Polling.WaitUntilAsync(() => gated.Entered >= 1, HangGuard);
        var waiting = Enumerable.Range(0, McpCrewToolHost.MaxWaitingCalls)
            .Select(i => host.CallToolAsync("run_crew_report_writer", ArgsFor($"topic{i}"), new RecordingProgress(), Ct))
            .ToList();
        await Polling.WaitUntilAsync(() => host.WaitingCalls("run_crew_report_writer") == McpCrewToolHost.MaxWaitingCalls, HangGuard);

        var refused = await host.CallToolAsync("run_crew_report_writer", ArgsFor("wasps"), new RecordingProgress(), Ct)
            .WaitAsync(HangGuard, Ct);

        Assert.True(refused.IsError);
        Assert.Contains("busy", Assert.Single(refused.Content).Text, StringComparison.Ordinal);
        gated.Release();
        var results = await Task.WhenAll([running, .. waiting]).WaitAsync(HangGuard, Ct);
        Assert.All(results, r => Assert.NotEqual(true, r.IsError));
    }

    [Fact]
    [Trait("BR", "FWK-24")]
    public async Task ShouldLetAWaitingCallGoWithoutRunning_WhenTheServerStopsAdmitting()
    {
        var gated = new FakeGatedAgentExecutionService();
        _service = gated;
        using var host = await OpenAsync(WriteCrew("a", "Report Writer", TwoTasks));
        var running = host.CallToolAsync("run_crew_report_writer", ArgsFor("bees"), new RecordingProgress(), Ct);
        await Polling.WaitUntilAsync(() => gated.Entered >= 1, HangGuard);
        var waiting = host.CallToolAsync("run_crew_report_writer", ArgsFor("wasps"), new RecordingProgress(), Ct);
        await Polling.WaitUntilAsync(() => host.WaitingCalls("run_crew_report_writer") == 1, HangGuard);

        host.StopAdmitting();
        var letGo = await waiting.WaitAsync(HangGuard, Ct);

        Assert.True(letGo.IsError);
        Assert.Contains("closing", Assert.Single(letGo.Content).Text, StringComparison.Ordinal);
        gated.Release();
        var first = await running.WaitAsync(HangGuard, Ct);
        Assert.Equal("Polish the draft, keep bees.", Assert.Single(first.Content).Text);
        // Two tasks of the one run: the call let go never ran the crew.
        Assert.Equal(2, gated.Entered);
        // A call read before the close that finds the crew free still runs: its client may still read the answer.
        var late = host.CallToolAsync("run_crew_report_writer", ArgsFor("ants"), new RecordingProgress(), Ct);
        await Polling.WaitUntilAsync(() => gated.Entered >= 3, HangGuard);
        gated.Release();
        Assert.False((await late.WaitAsync(HangGuard, Ct)).IsError);
    }

    [Fact]
    [Trait("BR", "FWK-24")]
    public async Task ShouldCancelTheRunningCrew_WhenTheCallIsCancelled()
    {
        var gated = new FakeGatedAgentExecutionService();
        _service = gated;
        using var host = await OpenAsync(WriteCrew("a", "Report Writer", TwoTasks));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var call = host.CallToolAsync("run_crew_report_writer", BeesArgs, new RecordingProgress(), cts.Token);
        await Polling.WaitUntilAsync(() => gated.Entered >= 1, HangGuard);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(HangGuard, Ct));
        Assert.Equal(1, gated.Cancelled);
    }

    private static readonly TimeSpan HangGuard = TimeSpan.FromMinutes(2);
}
