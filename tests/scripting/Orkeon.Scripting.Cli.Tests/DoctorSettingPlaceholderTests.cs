using System.Text.Json;
using Orkeon.Constants.Configuration;
using Orkeon.Hosting;
using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>CH-14: <c>orkeon doctor</c> judges a variable reference in a setting as the run does.</summary>
[Collection(CliCollection.Name)]
public sealed class DoctorSettingPlaceholderTests : IDisposable
{
    public DoctorSettingPlaceholderTests() =>
        RunnerSettings.GlobalSettingsPathOverride =
            Path.Combine(Path.GetTempPath(), "orkeon-doctor-no-global-" + Guid.NewGuid().ToString("N"), "appsettings.json");

    public void Dispose() =>
        RunnerSettings.GlobalSettingsPathOverride = AssemblyGlobalSettingsGuard.DefaultOverride;

    [Fact]
    [Trait("BR", "CH-14")]
    public async Task ShouldFailTheRunnerSettingsCheck_WhenAnMcpEnvEntryIsAReference()
    {
        const string path = "MCP:Servers:s:Env:GITHUB_TOKEN";
        using var scratch = new ScriptScratch();
        scratch.WriteFile("appsettings.json", """
            {
              "RaggableTree": { "Enabled": false },
              "MCP": { "Servers": { "s": { "Command": "demo-mcp", "Env": { "GITHUB_TOKEN": "${GITHUB_TOKEN}" } } } }
            }
            """);
        using var console = new TestConsole();

        var exit = await DoctorCommand.ExecuteAsync(new DoctorCommandOptions
        {
            Json = true,
            WorkingDirectoryOverride = scratch.Root,
        });

        Assert.Equal(1, exit);
        using var doc = JsonDocument.Parse(console.Stdout);
        var lines = doc.RootElement.EnumerateArray()
            .Where(e => e.GetProperty("check").GetString() == "runner-settings")
            .ToList();
        var line = Assert.Single(lines);
        Assert.Equal("fail", line.GetProperty("status").GetString());
        var detail = line.GetProperty("detail").GetString()!;
        Assert.Contains(path, detail, StringComparison.Ordinal);
        Assert.Contains(SettingPlaceholders.CorrectionFor(path)!, detail, StringComparison.Ordinal);
    }
}
