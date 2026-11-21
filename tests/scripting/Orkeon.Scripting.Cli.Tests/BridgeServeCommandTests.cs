using System.Net;
using System.Net.Http.Headers;
using Orkeon.Hosting.ToolBridge;
using Orkeon.Scripting.Cli.Commands;
using Orkeon.Tests.Shared.Network;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// #445 - <c>orkeon bridge serve</c>: the host built for real over a settings file in a scratch directory, the bridge
/// listening on a loopback port, stopped by a Ctrl+C the test plays.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class BridgeServeCommandTests
{
    private const string Token = "cli-bridge-token";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string? Environment(string name) => name == ToolBridgeProtocol.DefaultTokenEnvVar ? Token : null;

    private static string Settings(ScriptScratch scratch, Uri prefix, string allow) =>
        scratch.WriteFile("appsettings.json", $$"""{ "Orkeon": { "Bridge": { "Prefix": "{{prefix}}", "Allow": [{{allow}}] } } }""");

    /// <summary>Starts the verb in the background and waits until it listens; the task ends with its exit code.</summary>
    private static async Task<(Task<int> Exit, ToolBridgeServer Server)> StartAsync(TestConsole console, string settings)
    {
        var started = new TaskCompletionSource<ToolBridgeServer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = new BridgeServeCommandOptions { SettingsPath = settings, ReadEnvironment = Environment, Started = started.SetResult };
        var exit = BridgeCommand.ServeAsync(console, options);
        var first = await Task.WhenAny(started.Task, exit).WaitAsync(HangGuards.ProcessStart, Ct);
        Assert.True(first == started.Task, "the bridge did not start: " + console.Stderr);
        return (exit, await started.Task);
    }

    [Fact]
    public async Task ShouldListTheServedTools_WhenAskedWithTheToken()
    {
        // Arrange
        using var scratch = new ScriptScratch();
        var prefix = LoopbackPorts.BaseAddress(LoopbackPorts.Probe());
        using var console = new TestConsole();
        var (exit, _) = await StartAsync(console, Settings(scratch, prefix, "\"web_search\""));
        using var client = new HttpClient { BaseAddress = new Uri(prefix, ToolBridgeProtocol.BasePath) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        // Act
        var listing = await client.GetStringAsync(ToolBridgeProtocol.ToolsEndpoint, Ct);

        // Assert
        console.Interrupt();
        await exit;
        Assert.Contains("\"web_search\"", listing, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldNameThePrefixAndTheServedTools_WhenItListens()
    {
        // Arrange
        using var scratch = new ScriptScratch();
        var prefix = LoopbackPorts.BaseAddress(LoopbackPorts.Probe());
        using var console = new TestConsole();

        // Act
        var (exit, _) = await StartAsync(console, Settings(scratch, prefix, "\"web_search\""));

        // Assert
        console.Interrupt();
        await exit;
        Assert.Contains($"listening on {prefix}", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("serving web_search", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldExitZero_WhenCtrlCStopsIt()
    {
        // Arrange
        using var scratch = new ScriptScratch();
        var prefix = LoopbackPorts.BaseAddress(LoopbackPorts.Probe());
        using var console = new TestConsole();
        var (exit, server) = await StartAsync(console, Settings(scratch, prefix, "\"*\""));

        // Act
        console.Interrupt();
        var code = await exit.WaitAsync(HangGuards.ProcessStart, Ct);

        // Assert
        Assert.Equal(Program.ExitOk, code);
        Assert.False(server.IsRunning);
    }

    [Fact]
    public async Task ShouldRefuseToStart_WhenTheTokenVariableIsNotSet()
    {
        // Arrange
        using var scratch = new ScriptScratch();
        var settings = Settings(scratch, LoopbackPorts.BaseAddress(LoopbackPorts.Probe()), "\"*\"");
        using var console = new TestConsole();

        // Act
        var exit = await BridgeCommand.ServeAsync(console, new BridgeServeCommandOptions { SettingsPath = settings, ReadEnvironment = _ => null });

        // Assert
        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains(ToolBridgeProtocol.DefaultTokenEnvVar, console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldRefuseToStart_WhenAllowNamesNoToolOfTheHost()
    {
        // Arrange
        using var scratch = new ScriptScratch();
        var settings = Settings(scratch, LoopbackPorts.BaseAddress(LoopbackPorts.Probe()), "\"no_such_tool\"");
        using var console = new TestConsole();

        // Act
        var exit = await BridgeCommand.ServeAsync(console, new BridgeServeCommandOptions { SettingsPath = settings, ReadEnvironment = Environment });

        // Assert
        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("no_such_tool", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldRefuseToStart_WhenThePrefixIsHttpOffLoopback()
    {
        // Arrange
        using var scratch = new ScriptScratch();
        var settings = Settings(scratch, new Uri("http://192.0.2.10:7410/"), "\"*\"");
        using var console = new TestConsole();

        // Act
        var exit = await Program.DispatchAsync(console, ["bridge", "serve", "--settings", settings]);

        // Assert
        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("requires https", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldNameTheSettingsOption_WhenAskedForHelp()
    {
        // Arrange
        using var console = new TestConsole();

        // Act
        var exit = await Program.DispatchAsync(console, ["bridge", "serve", "--help"]);

        // Assert
        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains("--settings", console.Stdout + console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldAnswerUnauthorized_WhenTheTokenIsWrong()
    {
        // Arrange
        using var scratch = new ScriptScratch();
        var prefix = LoopbackPorts.BaseAddress(LoopbackPorts.Probe());
        using var console = new TestConsole();
        var (exit, _) = await StartAsync(console, Settings(scratch, prefix, "\"*\""));
        using var client = new HttpClient { BaseAddress = new Uri(prefix, ToolBridgeProtocol.BasePath) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-one");

        // Act
        using var response = await client.GetAsync(ToolBridgeProtocol.ToolsEndpoint, Ct);

        // Assert
        console.Interrupt();
        await exit;
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
