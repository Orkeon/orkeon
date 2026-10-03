using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// STUDIO-50 on the scripting path: a procedural <c>.ork.ts</c> builds its host itself, and an
/// <c>--llm-profile</c> the settings do not define is refused there too — in one line, listing the
/// profiles they do, before any host or esbuild, with the exit code of a configuration mistake.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class RunCommandLlmProfileTests
{
    [Fact]
    public async Task A_procedural_script_electing_an_unknown_profile_is_refused_before_any_host()
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("ok.ork.ts", """
            /// <reference orkeon-script="1.0" />
            """);
        var settings = scratch.WriteFile("appsettings.json", """
            { "Llm": { "Model": "model-default", "Profiles": { "b": { "Model": "model-b" } } } }
            """);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = script,
            SettingsPath = settings,
            LlmProfile = "nope",
        });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("ERROR: --llm-profile names the LLM profile 'nope'", console.Stderr, StringComparison.Ordinal);
        Assert.Contains("Known profiles: default, b.", console.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("esbuild", console.Stderr, StringComparison.OrdinalIgnoreCase);
    }
}
