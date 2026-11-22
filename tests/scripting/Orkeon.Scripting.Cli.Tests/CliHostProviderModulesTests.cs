using System.Text.Json;
using Orkeon.Scripting.Cli.Commands;
using Orkeon.Scripting.Cli.Commands.Forge;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// #472: the hosts of <c>email</c>, <c>drive</c>, <c>runs</c>, <c>rag</c> and <c>forge rephrase</c> register the
/// provider modules the tool ships, so a settings file <c>orkeon run</c> accepts — one that names the
/// <c>agent-cli</c> provider, or carries its <c>Llm:AgentCli</c> section — is accepted by each of them.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class CliHostProviderModulesTests
{
    /// <summary>The issue's scenario A: the default LLM is the module's provider.</summary>
    private const string NamesTheModule = """
        { "RaggableTree": { "Enabled": false }, "Llm": { "Provider": "agent-cli", "Model": "claude-sonnet-5-5" } }
        """;

    /// <summary>The issue's scenario B: another provider, beside the module's own section.</summary>
    private const string CarriesItsSection = """
        { "RaggableTree": { "Enabled": false },
          "Llm": { "Model": "m", "BaseUrl": "http://localhost:11434/v1", "AgentCli": { "Command": "claude" } } }
        """;

    public static TheoryData<string> Settings => [NamesTheModule, CarriesItsSection];

    [Theory]
    [MemberData(nameof(Settings))]
    [Trait("BR", "CH-472")]
    public async Task EmailAccounts_ShouldAcceptTheSettings_WhenTheyNameTheAgentCliModule(string settings)
    {
        using var scratch = new ScriptScratch();
        scratch.WriteFile("appsettings.json", settings);
        using var console = new TestConsole();

        var exit = await EmailCommand.ExecuteAccountsAsync(
            console, new EmailAccountsCommandOptions { WorkingDirectoryOverride = scratch.Root, Json = true });

        Assert.Equal(Program.ExitOk, exit);
        Assert.Equal(JsonValueKind.Array, JsonElement.Parse(console.Stdout).ValueKind);
    }

    [Theory]
    [MemberData(nameof(Settings))]
    [Trait("BR", "CH-472")]
    public async Task DriveAccounts_ShouldAcceptTheSettings_WhenTheyNameTheAgentCliModule(string settings)
    {
        using var scratch = new ScriptScratch();
        scratch.WriteFile("appsettings.json", settings);
        using var console = new TestConsole();

        var exit = await DriveCommand.ExecuteAccountsAsync(
            console, new DriveAccountsCommandOptions { WorkingDirectoryOverride = scratch.Root, Json = true });

        Assert.Equal(Program.ExitOk, exit);
        Assert.Equal(JsonValueKind.Array, JsonElement.Parse(console.Stdout).ValueKind);
    }

    /// <summary>The host is built: the verb gets as far as its own refusal, a run history that is not enabled.</summary>
    [Theory]
    [MemberData(nameof(Settings))]
    [Trait("BR", "CH-472")]
    public async Task RunsList_ShouldBuildItsHost_WhenTheSettingsNameTheAgentCliModule(string settings)
    {
        using var scratch = new ScriptScratch();
        var path = scratch.WriteFile("appsettings.json", settings);
        using var console = new TestConsole(stdin: string.Empty);

        await RunsCommand.DispatchAsync(console, ["list", "--settings", path], scratch.Root);

        Assert.Contains("the run history is not enabled", console.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("Llm:", console.Stderr, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Settings))]
    [Trait("BR", "CH-472")]
    public void RagHost_ShouldBuild_WhenTheSettingsNameTheAgentCliModule(string settings)
    {
        using var scratch = new ScriptScratch();
        var path = scratch.WriteFile("appsettings.json", settings);
        using var console = new TestConsole();

        using var host = RagCommand.BuildHost(console, new RagSearchCommandOptions { SettingsPath = path }, scratch.Root);

        Assert.NotNull(host);
    }

    /// <summary>
    /// The module's section alone names no default LLM: the verb's own refusal is reached
    /// (<c>FORGE-LLM-UNAVAILABLE</c>), where the host refused <c>Llm:AgentCli</c> as no setting.
    /// </summary>
    [Fact]
    [Trait("BR", "CH-472")]
    public async Task ForgeRephrase_ShouldBuildItsHost_WhenTheSettingsCarryTheAgentCliSection()
    {
        using var scratch = new ScriptScratch();
        var path = scratch.WriteFile("settings.json", """{ "Llm": { "AgentCli": { "Command": "claude" } } }""");
        using var console = new TestConsole();

        var exit = await ForgeCommand.DispatchAsync(console, ["rephrase", "un besoin", "--events", "jsonl", "--settings", path], scratch.Root);

        Assert.Equal(1, exit);
        var error = JsonElement.Parse(Assert.Single(
            console.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries),
            line => line.Contains("\"kind\":\"error\"", StringComparison.Ordinal)));
        Assert.Equal(ForgeRephrase.LlmUnavailableCode, error.GetProperty("code").GetString());
    }
}
