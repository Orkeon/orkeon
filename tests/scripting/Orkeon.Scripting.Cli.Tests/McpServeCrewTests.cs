using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Scripting.Cli.Commands;
using Orkeon.Scripting.Cli.Tests.Doubles;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// FWK-24 - <c>orkeon mcp serve --crew</c> driven the way a client drives it: JSON-RPC on stdin, the
/// answers and the progress notifications on stdout, which carries nothing else. The crew is loaded for real, from a
/// file, through the entry point <c>orkeon run</c> loads crews with.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class McpServeCrewTests
{
    private const string Crew = """
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
          draft:
            description: "Draft a text about {topic}."
            expectedOutput: "A draft."
            agent: "worker"
          polish:
            description: "Polish the draft."
            expectedOutput: "Polished."
            agent: "worker"
            dependencies: ["draft"]
        """;

    private static async Task<(int Exit, TestConsole Console)> ServeAsync(ScriptScratch scratch, string stdin, params string[] crews)
    {
        var console = new TestConsole(stdin: stdin);
        var settings = scratch.WriteFile("appsettings.json", """{ "RaggableTree": { "Enabled": false } }""");
        var exit = await McpCommand.ServeAsync(console, new McpServeCommandOptions
        {
            SettingsPath = settings,
            Crews = crews,
            AllowExternalMounts = true,
            WorkingDirectoryOverride = scratch.Root,
            ConfigureCrewServices = (_, services) => services.AddScoped<IAgentExecutionService>(_ => new FakeAgentExecutionService()),
        });
        return (exit, console);
    }

    private static List<JsonElement> Lines(string stdout) =>
        stdout.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0)
            .Select(line =>
            {
                using var document = JsonDocument.Parse(line);
                Assert.Equal("2.0", document.RootElement.GetProperty("jsonrpc").GetString());
                return document.RootElement.Clone();
            }).ToList();

    [Fact]
    [Trait("BR", "FWK-24")]
    public async Task Serve_lists_the_crew_as_a_tool_and_its_tasks_as_prompts()
    {
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteFile("crew.yaml", Crew);
        const string stdin =
            """
            {"jsonrpc":"2.0","id":1,"method":"tools/list"}
            {"jsonrpc":"2.0","id":2,"method":"prompts/list"}
            {"jsonrpc":"2.0","id":3,"method":"prompts/get","params":{"name":"report_writer_task_1","arguments":{"topic":"bees"}}}

            """;

        var (exit, console) = await ServeAsync(scratch, stdin, crew);
        using var _ = console;

        Assert.Equal(Program.ExitOk, exit);
        var answers = Lines(console.Stdout).OrderBy(a => a.GetProperty("id").GetInt32()).ToList();
        var tool = answers[0].GetProperty("result").GetProperty("tools").EnumerateArray()
            .Single(t => t.GetProperty("name").GetString() == "run_crew_report_writer");
        Assert.Equal("topic", Assert.Single(tool.GetProperty("inputSchema").GetProperty("properties").EnumerateObject()).Name);
        Assert.Equal(["report_writer_task_1", "report_writer_task_2"],
            answers[1].GetProperty("result").GetProperty("prompts").EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToList());
        Assert.Contains("Draft a text about bees.",
            answers[2].GetProperty("result").GetProperty("messages")[0].GetProperty("content").GetProperty("text").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-24")]
    public async Task A_call_runs_the_crew_and_streams_one_progress_per_task_before_the_answer()
    {
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteFile("crew.yaml", Crew);
        const string stdin =
            """{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"run_crew_report_writer","arguments":{"topic":"bees"},"_meta":{"progressToken":"p1"}}}""" + "\n";

        var (exit, console) = await ServeAsync(scratch, stdin, crew);
        using var _ = console;

        Assert.Equal(Program.ExitOk, exit);
        var lines = Lines(console.Stdout);
        Assert.Equal(3, lines.Count);
        Assert.All(lines.Take(2), line => Assert.Equal("notifications/progress", line.GetProperty("method").GetString()));
        Assert.Equal([1d, 2d], lines.Take(2).Select(l => l.GetProperty("params").GetProperty("progress").GetDouble()).ToList());
        Assert.Equal(7, lines[2].GetProperty("id").GetInt32());
        var result = lines[2].GetProperty("result");
        Assert.False(result.TryGetProperty("isError", out var isError) && isError.GetBoolean());
        Assert.Equal("out:polish", result.GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    [Trait("BR", "FWK-24")]
    public async Task A_crew_that_does_not_load_refuses_the_start_and_stdout_stays_empty()
    {
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteFile("broken.yaml", "name: [unclosed");

        var (exit, console) = await ServeAsync(scratch, """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""" + "\n", crew);
        using var _ = console;

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Empty(console.Stdout);
        Assert.Contains("broken.yaml", console.Stderr, StringComparison.Ordinal);
    }
    [Fact]
    [Trait("BR", "FWK-24")]
    public async Task A_cancelled_notification_cancels_the_running_crew_and_the_call_is_not_answered()
    {
        var hangGuard = TimeSpan.FromMinutes(2);
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteFile("crew.yaml", Crew);
        var settings = scratch.WriteFile("appsettings.json", """{ "RaggableTree": { "Enabled": false } }""");
        var model = new FakeGatedAgentExecutionService();
        using var console = new FakeGatedStdinConsole();

        var serve = McpCommand.ServeAsync(console, new McpServeCommandOptions
        {
            SettingsPath = settings,
            Crews = [crew],
            AllowExternalMounts = true,
            WorkingDirectoryOverride = scratch.Root,
            ConfigureCrewServices = (_, services) => services.AddScoped<IAgentExecutionService>(_ => model),
        });
        try
        {
            console.Send("""{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"run_crew_report_writer","arguments":{"topic":"bees"}}}""");
            await Polling.WaitUntilAsync(() => model.Entered >= 1, hangGuard);
            console.Send("""{"jsonrpc":"2.0","method":"notifications/cancelled","params":{"requestId":5,"reason":"test"}}""");
            await Polling.WaitUntilAsync(() => model.Cancelled >= 1, hangGuard);
        }
        finally
        {
            console.CompleteInput();
        }

        var exit = await serve.WaitAsync(hangGuard, TestContext.Current.CancellationToken);

        Assert.Equal(Program.ExitOk, exit);
        Assert.Equal(1, model.Cancelled);
        Assert.Empty(console.Stdout);
    }
}
