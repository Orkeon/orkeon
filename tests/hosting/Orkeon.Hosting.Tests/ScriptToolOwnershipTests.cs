using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces;
using Orkeon.Domain.Agent;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// A tool a <c>.ork.ts</c> crew defines belongs to the load that defined it: <c>orkeon-host</c> loads the
/// crew once per message on one host, and two loads, concurrent or not, of one crew or of two, neither
/// refuse nor call each other's instance. Joins <see cref="ConsoleSerialCollection"/> because the host
/// warns on the process-global stderr.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class ScriptToolOwnershipTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-script-tools-" + Guid.NewGuid().ToString("N"));
    private readonly IHost _host;

    public ScriptToolOwnershipTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "appsettings.json"), "{ \"RaggableTree\": { \"Enabled\": false } }");
        WriteCrew("first");
        WriteCrew("second");
        _host = RunnerHost.Build(
            Path.Combine(_root, "appsettings.json"),
            new RunnerMountPlan { CliMounts = [$"{FileSystemMount.Quote(_root)}:/crews:ro"], AllowExternalMounts = true });
    }

    public void Dispose()
    {
        _host.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best-effort temp cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort temp cleanup */ }
    }

    [Fact]
    public async Task ShouldLoadTheCrewAgain_WhenTheHostAlreadyLoadedItOnce()
    {
        using var firstRun = _host.Services.CreateScope();
        using var secondRun = _host.Services.CreateScope();

        var first = await LoadToolAsync(firstRun, "first");
        var second = await LoadToolAsync(secondRun, "first");

        Assert.NotSame(first, second);
        // Each run has its own engine: the counter of one is not the other's.
        Assert.Contains("first:1", await CallAsync(first), StringComparison.Ordinal);
        Assert.Contains("first:2", await CallAsync(first), StringComparison.Ordinal);
        Assert.Contains("first:1", await CallAsync(second), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldGiveEachRunItsOwnInstance_WhenTheRunsAreConcurrent()
    {
        const int runs = 4;
        var scopes = Enumerable.Range(0, runs).Select(_ => _host.Services.CreateScope()).ToList();
        try
        {
            var tools = await Task.WhenAll(scopes.Select(scope => Task.Run(() => LoadToolAsync(scope, "first"))));
            var answers = await Task.WhenAll(tools.Select(tool => Task.Run(() => CallAsync(tool))));

            Assert.Equal(runs, tools.Distinct().Count());
            Assert.All(answers, answer => Assert.Contains("first:1", answer, StringComparison.Ordinal));
        }
        finally
        {
            scopes.ForEach(scope => scope.Dispose());
        }
    }

    [Fact]
    public async Task ShouldCallTheToolOfItsOwnScript_WhenTwoCrewsDefineTheSameName()
    {
        using var firstRun = _host.Services.CreateScope();
        using var secondRun = _host.Services.CreateScope();

        var first = await LoadToolAsync(firstRun, "first");
        var second = await LoadToolAsync(secondRun, "second");

        Assert.Contains("first:1", await CallAsync(first), StringComparison.Ordinal);
        Assert.Contains("second:1", await CallAsync(second), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldLeaveTheHostRegistryAsItWas_WhenACrewWithItsOwnToolsIsLoaded()
    {
        var registry = _host.Services.GetRequiredService<IToolRegistry>();
        var before = await NamesAsync(registry);

        using var run = _host.Services.CreateScope();
        await LoadToolAsync(run, "first");

        Assert.Equal(before, await NamesAsync(registry));
    }

    [Theory]
    [InlineData("json_tool")]
    [InlineData("JSON_TOOL")]
    [InlineData(CoworkerDelegation.DelegateWorkToolName)]
    public async Task ShouldRefuseTheLoad_WhenTheScriptToolTakesANameOfTheHost(string toolName)
    {
        WriteCrew("impostor", toolName);
        using var run = _host.Services.CreateScope();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => LoadToolAsync(run, "impostor"));

        Assert.Contains($"Script tool '{toolName}' collides", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldRefuseTheLoad_WhenOneScriptDefinesTwoToolsOfTheSameName()
    {
        WriteCrew("twice", extraTool: "Counter");
        using var run = _host.Services.CreateScope();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => LoadToolAsync(run, "twice"));

        Assert.Contains("Script tool 'Counter' collides", error.Message, StringComparison.Ordinal);
    }

    private static async Task<List<string>> NamesAsync(IToolRegistry registry) =>
        [.. (await registry.GetAllToolsAsync(TestContext.Current.CancellationToken)).Select(t => t.Name).Order(StringComparer.Ordinal)];

    private async Task<IBaseTool> LoadToolAsync(IServiceScope scope, string crewName)
    {
        var ct = TestContext.Current.CancellationToken;
        var services = scope.ServiceProvider;
        var crew = await RunnerExecution.LoadCrewAsync(
            _host,
            services.GetRequiredService<ICrewFactory>(),
            services,
            $"/crews/{crewName}.ork.ts",
            NullLogger.Instance,
            ct);
        var agents = await services.GetRequiredService<IAgentRepository>().GetByIdsAsync(crew.Agents, ct);
        return Assert.Single(Assert.Single(agents).Tools, tool => tool.Name == "counter");
    }

    private static async Task<string> CallAsync(IBaseTool tool)
    {
        var result = await tool.ExecuteAsync("""{ "n": 1 }""", TestContext.Current.CancellationToken);
        Assert.True(result.Success, result.Error);
        return result.Output ?? string.Empty;
    }

    private void WriteCrew(string label, string toolName = "counter", string? extraTool = null)
    {
        const string schema = """{ type: "object", properties: { n: { type: "number", description: "n" } }, required: ["n"] }""";
        var extra = extraTool is null
            ? string.Empty
            : $$""".withAutonomousTool(toolBuilder().name("{{extraTool}}").description("Counts too").withSchema({{schema}}).execute(() => "other").build())""";
        File.WriteAllText(Path.Combine(_root, label + ".ork.ts"), $$"""
            /// <reference orkeon-script="1.0" />
            let calls = 0;
            const counter = toolBuilder()
                .name("{{toolName}}")
                .description("Counts its calls")
                .withSchema({{schema}})
                .execute(() => "{{label}}:" + (++calls))
                .build();
            const analyst = agentBuilder().name("analyst").role("Analyst").goal("Count")
                .withAutonomousTool(counter){{extra}}.build();
            const work = taskBuilder().agent(analyst).description("Count").expectedOutput("A number").build();
            const crew = crewBuilder().name("{{label}}").goal("Own its tools")
                .withAgent(analyst).withTask(work).build();
            (globalThis as any).crew = crew;
            """);
    }
}
