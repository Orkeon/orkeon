using Orkeon.Scripting.Cli.Commands;
using Orkeon.Tests.Shared.Network;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>FWK-16: <c>orkeon run</c> of a script shows the sign-in code of an <c>oauth2-device</c> MCP server on stderr.</summary>
public sealed class RunCommandMcpDeviceFlowTests
{
    [Fact]
    public async Task ShouldShowTheDeviceCodeOnStderr_WhenTheScriptsSettingsDeclareADeviceFlowServer()
    {
        await using var idp = new FakeDeviceAuthorizationServer();
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("device.ork.ts", "/// <reference orkeon-script=\"1.0\" />\nvar result = 42;\n");
        var settings = scratch.WriteFile("appsettings.json", $$"""
            { "MCP": { "Servers": { "tracker": {
                "Transport": "Sse", "Url": "http://127.0.0.1:9/mcp/",
                "Auth": { "Type": "oauth2-device", "ClientId": "orkeon-cli",
                          "TokenUrl": "{{idp.TokenUrl}}", "DeviceAuthorizationUrl": "{{idp.DeviceUrl}}" } } } } }
            """);
        using var console = new TestConsole();

        await RunCommand.ExecuteAsync(console, new RunCommandOptions { ScriptPath = script, SettingsPath = settings });

        Assert.Equal(1, idp.DeviceRequests);
        Assert.Contains(FakeDeviceAuthorizationServer.UserCode, console.Stderr, StringComparison.Ordinal);
        Assert.Contains(idp.VerificationUrl.ToString(), console.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeDeviceAuthorizationServer.UserCode, console.Stdout, StringComparison.Ordinal);
    }
}
