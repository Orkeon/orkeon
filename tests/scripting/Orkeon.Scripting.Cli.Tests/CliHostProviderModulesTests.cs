using System.Text.Json;
using Orkeon.Scripting.Cli.Commands;
using Orkeon.Scripting.Cli.Commands.Forge;
using Orkeon.Tests.Shared.Produced;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// #472: the hosts of <c>email</c>, <c>drive</c>, <c>runs</c>, <c>rag</c> and <c>forge rephrase</c>, like every host the tool composes, register the
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
        var path = scratch.WriteFile("appsettings.json", settings);
        using var console = new TestConsole();

        var exit = await EmailCommand.ExecuteAccountsAsync(
            console, new EmailAccountsCommandOptions { WorkingDirectoryOverride = scratch.Root, Json = true });

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains($"Using settings: {path}", console.Stderr, StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Array, JsonElement.Parse(console.Stdout).ValueKind);
    }

    [Theory]
    [MemberData(nameof(Settings))]
    [Trait("BR", "CH-472")]
    public async Task DriveAccounts_ShouldAcceptTheSettings_WhenTheyNameTheAgentCliModule(string settings)
    {
        using var scratch = new ScriptScratch();
        var path = scratch.WriteFile("appsettings.json", settings);
        using var console = new TestConsole();

        var exit = await DriveCommand.ExecuteAccountsAsync(
            console, new DriveAccountsCommandOptions { WorkingDirectoryOverride = scratch.Root, Json = true });

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains($"Using settings: {path}", console.Stderr, StringComparison.Ordinal);
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

        var exit = await RunsCommand.DispatchAsync(console, ["list", "--settings", path], scratch.Root);

        Assert.Equal(Program.ExitScriptError, exit);
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

    /// <summary>The host alone, never the verb: on these files the verb would call a model.</summary>
    [Theory]
    [MemberData(nameof(Settings))]
    [Trait("BR", "CH-472")]
    public void ForgeRephraseHost_ShouldBuild_WhenTheSettingsNameTheAgentCliModule(string settings)
    {
        using var scratch = new ScriptScratch();
        var path = scratch.WriteFile("settings.json", settings);
        using var console = new TestConsole();

        using var host = ForgeRephrase.BuildHost(console, path, events: null);

        Assert.NotNull(host);
    }

    /// <summary>
    /// The guard of the hosts to come: a file of the tool that composes a host (the runner's, a crew's,
    /// a tool host or the settings inspection) registers the provider modules, by itself or through
    /// <c>RunCommand.AddCliRagServices</c>. It reads the sources: a host built without them compiles and
    /// refuses only the settings files that name a module.
    /// </summary>
    [Fact]
    [Trait("BR", "CH-472")]
    public void EveryFileOfTheToolThatComposesAHost_ShouldRegisterTheProviderModules()
    {
        string[] composes =
        [
            "RunnerHost.Build(", "RunnerHost.InspectSettings(", "RunnerExecution.RunOneShotAsync(",
            "RunnerExecution.BuildToolHostAsync(", "CrewRunEntryPoint.TryOpen(", "McpCrewToolHost.OpenAsync(",
        ];
        string[] registers = ["CliLlmProviderModules.AddTo(", "AddCliRagServices("];
        var root = ProducedFile.RepositoryRoot();

        var sources = RepositoryFiles.Under(["src/scripting/Orkeon.Scripting.Cli"], path => path.EndsWith(".cs", StringComparison.Ordinal))
            .Select(path => (Path: path, Text: File.ReadAllText(Path.Combine(root, path))))
            .Where(file => composes.Any(call => file.Text.Contains(call, StringComparison.Ordinal)))
            .ToList();

        Assert.True(sources.Count >= 11, $"only {sources.Count} files of the tool compose a host: the guard reads the wrong folder.");
        Assert.Empty(sources.Where(file => !registers.Any(call => file.Text.Contains(call, StringComparison.Ordinal))).Select(file => file.Path));
    }
}
