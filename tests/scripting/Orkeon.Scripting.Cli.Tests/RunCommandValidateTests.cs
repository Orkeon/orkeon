using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// #316: <c>orkeon run --validate</c> on a <c>.ork.ts</c> answers what <c>orkeon run</c> would do with it.
/// A script whose source never assigns <c>globalThis.crew</c> (it publishes its crew through a helper, or
/// runs it itself) runs on the scripting host, so validation does not judge it by the declarative
/// loader's checks; a script that assigns it is still loaded as a crew definition.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class RunCommandValidateTests
{
    private const string Settings = """{ "RaggableTree": { "Enabled": false } }""";

    // The handoff sits in an imported helper, as in a project that wraps it: the entry's source never
    // names globalThis.crew, so `orkeon run` evaluates the script on the scripting host.
    private const string Publish = "export function publish(crew: any): void { (globalThis as any).crew = crew; }\n";

    private static async Task<(int Exit, TestConsole Console)> ValidateAsync(ScriptScratch scratch, string script)
    {
        var settings = scratch.WriteFile("appsettings.json", Settings);
        var console = new TestConsole();
        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = script,
            SettingsPath = settings,
            AllowExternalMounts = true,
            Validate = true,
        });
        return (exit, console);
    }

    [Fact]
    public async Task ShouldAcceptACrewWithNoTask_WhenTheScriptPublishesItThroughAHelper()
    {
        using var scratch = new ScriptScratch();
        scratch.WriteScript("publish.ts", Publish);
        var script = scratch.WriteScript("crew.ork.ts", """
            // @orkeon-script 1.0
            import { publish } from "./publish.ts";
            const committer = agentBuilder().name("c").role("r").goal("g").body(async () => "done").build();
            publish(crewBuilder().name("bodies").process("sequential").withAgent(committer).build());
            """);

        var (exit, console) = await ValidateAsync(scratch, script);
        using var _ = console;

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains("(procedural script: bundled, not run)", console.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("At least one task is required", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldAcceptAContextTokensBudget_WhenAnAutonomousCrewIsPublishedThroughAHelper()
    {
        using var scratch = new ScriptScratch();
        scratch.WriteScript("publish.ts", Publish);
        var script = scratch.WriteScript("crew.ork.ts", """
            // @orkeon-script 1.0
            import { publish } from "./publish.ts";
            const worker = agentBuilder().name("w").role("r").goal("g").build();
            publish(crewBuilder().name("loop").process("autonomous").withAgent(worker)
                .budget({ toolCalls: 200, wallTime: 1800, contextTokens: 700_000, delegationDepth: 2 }).build());
            """);

        var (exit, console) = await ValidateAsync(scratch, script);
        using var _ = console;

        Assert.Equal(Program.ExitOk, exit);
        Assert.DoesNotContain("contextTokens", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldStillLoadTheCrew_WhenTheScriptAssignsGlobalThisCrew()
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("crew.ork.ts", """
            // @orkeon-script 1.0
            (globalThis as any).crew = crewBuilder().name("empty").build();
            """);

        var (exit, console) = await ValidateAsync(scratch, script);
        using var _ = console;

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("At least one task is required", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldFailValidation_WhenAProceduralScriptDoesNotBundle()
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("crew.ork.ts", """
            // @orkeon-script 1.0
            import { publish } from "./missing.ts";
            publish(crewBuilder().build());
            """);

        var (exit, console) = await ValidateAsync(scratch, script);
        using var _ = console;

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("VALIDATION FAILED", console.Stderr, StringComparison.Ordinal);
    }
}
