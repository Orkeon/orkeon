using Microsoft.Extensions.DependencyInjection;
using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Tests.Shared.Network;
using Orkeon.Tools.Abstractions.Auth;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// FWK-16: a server in <c>oauth2-device</c> shows its sign-in code on the runner's error output in
/// <c>--validate</c> and <c>--list-tools</c> (which connect the servers), and the interaction behind it is
/// the console one. The tests go through the real entry points against a loopback identity provider.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class McpDeviceFlowWiringTests : IDisposable
{
    private sealed class TestOptions : RunnerOptionsBase { }

    private readonly string _tempDir = Directory.CreateTempSubdirectory("orkeon-hosting-mcp-device-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* best-effort temp cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort temp cleanup */ }
    }

    private string Settings(FakeDeviceAuthorizationServer idp)
    {
        var path = Path.Combine(_tempDir, "appsettings.json");
        File.WriteAllText(path, $$"""
            {
              "RaggableTree": { "Enabled": false },
              "MCP": { "Servers": { "tracker": {
                "Transport": "Sse", "Url": "http://127.0.0.1:9/mcp/",
                "Auth": { "Type": "oauth2-device", "ClientId": "orkeon-cli",
                          "TokenUrl": "{{idp.TokenUrl}}", "DeviceAuthorizationUrl": "{{idp.DeviceUrl}}" } } } }
            }
            """);
        return path;
    }

    [Fact]
    public async Task Validate_shows_the_device_code_on_the_error_output()
    {
        await using var idp = new FakeDeviceAuthorizationServer();
        var config = Path.Combine(_tempDir, "config.yaml");
        await File.WriteAllTextAsync(config, """
            name: "device-validate"
            goal: "g"
            process: "sequential"
            agents:
              reader:
                role: "Reader"
                goal: "Read"
                backstory: "b"
                tools:
                  - "file_read"
                maxIter: 1
            tasks:
              do_read:
                description: "d"
                expectedOutput: "o"
                agent: "reader"
            """, TestContext.Current.CancellationToken);
        var settings = Settings(idp);
        var console = new FakeRunnerConsole();
        var opts = new TestOptions { ConfigPath = config, SettingsPath = settings, AllowExternalMounts = true, RunnerConsole = console };

        await RunnerExecution.RunValidateAsync(opts, "Orkeon.Hosting.Tests", externalCt: TestContext.Current.CancellationToken);

        Assert.Equal(1, idp.DeviceRequests);
        var shown = console.ErrorOutput.ToString();
        Assert.Contains(FakeDeviceAuthorizationServer.UserCode, shown, StringComparison.Ordinal);
        Assert.Contains(idp.VerificationUrl.ToString(), shown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListTools_shows_the_device_code_on_the_error_output()
    {
        await using var idp = new FakeDeviceAuthorizationServer();
        var console = new FakeRunnerConsole();
        var opts = new TestOptions { SettingsPath = Settings(idp), AllowExternalMounts = true, RunnerConsole = console };

        await RunnerExecution.RunListToolsAsync(opts, "Orkeon.Hosting.Tests");

        Assert.Equal(1, idp.DeviceRequests);
        var shown = console.ErrorOutput.ToString();
        Assert.Contains(FakeDeviceAuthorizationServer.UserCode, shown, StringComparison.Ordinal);
        Assert.Contains(idp.VerificationUrl.ToString(), shown, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeDeviceAuthorizationServer.UserCode, console.Output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_console_interaction_writes_the_code_and_the_address_to_the_error_output_only()
    {
        var console = new FakeRunnerConsole();
        var interaction = new McpConsoleLoginInteraction(console);

        await interaction.ShowDeviceCodeAsync(
            "tracker", new Uri("https://idp.example.test/activate"), "ABCD-EFGH", TimeSpan.FromMinutes(10), TestContext.Current.CancellationToken);

        var error = console.ErrorOutput.ToString();
        Assert.Contains("tracker", error, StringComparison.Ordinal);
        Assert.Contains("https://idp.example.test/activate", error, StringComparison.Ordinal);
        Assert.Contains("ABCD-EFGH", error, StringComparison.Ordinal);
        Assert.Equal("", console.Output.ToString());
    }

    [Fact]
    public void RegisterMcpLoginInteraction_provides_the_console_interaction()
    {
        var services = new ServiceCollection();

        RunnerHost.RegisterMcpLoginInteraction(services, new FakeRunnerConsole());

        using var provider = services.BuildServiceProvider();
        Assert.IsType<McpConsoleLoginInteraction>(provider.GetRequiredService<IOAuthLoginInteraction>());
    }
}
