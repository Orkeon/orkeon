using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Hosting;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Scripting.Cli.Commands;
using Orkeon.Scripting.Cli.Commands.Run;
using Orkeon.Scripting.Cli.Events;
using Orkeon.Scripting.Cli.Tests.Doubles;
using Orkeon.Scripting.Cli.Tests.Forge;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>
/// FWK-05 through the real entry point: a crew whose agent runs on a profile that names a fallback,
/// observed the way <c>orkeon run --events jsonl</c> observes it. The primary profile answers 503, the
/// call moves to the secondary, the run succeeds, and the stream carries the llm.fallback line.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class LlmFallbackRunEventTests : IDisposable
{
    private readonly StringWriter _output = new();

    public void Dispose() => _output.Dispose();

    private const string Crew =
        """
        name: fallback-crew
        goal: Answer once
        process: sequential

        agents:
          writer:
            role: "Writer"
            goal: "Write"
            backstory: "An agent whose first provider is down."
            llm:
              profile: primary

        tasks:
          draft:
            description: "Draft it."
            expected_output: "A draft."
            agent: writer
        """;

    [Fact]
    public async Task A_503_on_the_primary_profile_moves_the_run_to_the_fallback_and_the_stream_says_so()
    {
        var down = new FakeHttpFailureLlmProvider();
        var up = new FakeBillingLlmProvider { Answer = "from-secondary" };
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript("crew.yaml", Crew);
        using var console = new TestConsole(stdin: string.Empty);
        await using var observed = new ObservedRunContext(
            new OrkeonEventWriter(_output, new FakeOrkeonClock()), stream: false, clientName: "studio", console.Input);

        var exit = await RunnerExecution.RunOneShotAsync(
            RunCommand.ToRunnerOptions(console, new RunCommandOptions { ScriptPath = crew, AllowExternalMounts = true, Events = "jsonl" }),
            "orkeon",
            configureServices: (_, services) =>
            {
                services.AddOrkeonLlmProfile("primary", _ => down, null, ["secondary"]);
                services.AddOrkeonLlmProfile("secondary", _ => up);
                observed.WireServices(services);
            },
            TestContext.Current.CancellationToken);
        await observed.FinishAsync(exit);

        Assert.Equal(0, exit);
        Assert.Equal(1, down.Calls);
        Assert.Equal(1, up.Calls);
        var fallback = _output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonElement.Parse(line))
            .Single(e => e.GetProperty("kind").GetString() == "llm.fallback");
        Assert.Equal("primary", fallback.GetProperty("profile").GetString());
        Assert.Equal("primary", fallback.GetProperty("from").GetString());
        Assert.Equal("secondary", fallback.GetProperty("to").GetString());
        Assert.Equal("server_error", fallback.GetProperty("reason").GetString());
    }
}
