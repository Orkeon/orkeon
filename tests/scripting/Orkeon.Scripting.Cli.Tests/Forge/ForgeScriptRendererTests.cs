using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Scripting;
using Orkeon.Scripting.Adapters;
using Orkeon.Scripting.Cli.Commands.Forge;
using Orkeon.Scripting.Toolchain;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>
/// The <c>.ork.ts</c> render (SPEC-ORKEON-FORGE §8.2-8.3). The load-bearing test is the
/// real round-trip: the rendered file reloaded through <c>ScriptHost</c> →
/// <c>JsCrewConfigurationAdapter</c> → the same <c>CrewDefinitionValidator</c> as the YAML
/// path — the template is deliberately TypeScript-compatible plain JavaScript, so the
/// round-trip runs offline (pass-through) while `orkeon run` uses the real toolchain.
/// </summary>
public sealed class ForgeScriptRendererTests : IDisposable
{
    private readonly string _sessionDirectory =
        Path.Combine(Path.GetTempPath(), "orkeon-forge-script-" + Guid.NewGuid().ToString("N"));

    public ForgeScriptRendererTests() => Directory.CreateDirectory(_sessionDirectory);

    public void Dispose()
    {
        if (Directory.Exists(_sessionDirectory))
            Directory.Delete(_sessionDirectory, recursive: true);
    }

    private static ForgeBlueprint Parse(string json)
    {
        Assert.True(ForgeBlueprint.TryParse(json, out var blueprint, out var errors), string.Join("; ", errors));
        return blueprint!;
    }

    private async Task<Orkeon.Domain.Configuration.CrewConfiguration> ReloadAsync()
    {
        var engineFactory = new JsEngineFactory(
            loggerFactory: NullLoggerFactory.Instance,
            configuration: new ConfigurationBuilder().Build(),
            builtInTools: []);
        var scriptHost = new ScriptHost(
            new DiskBackedFileSystemService(_sessionDirectory, "/forge"),
            PassThroughTranspiler.Instance,
            engineFactory,
            NullLogger<ScriptHost>.Instance);

        var jsCrew = await scriptHost.LoadCrewFromFileAsync(
            Path.Combine(_sessionDirectory, "crew", ForgeScriptRenderer.ScriptFileName),
            $"/forge/crew/{ForgeScriptRenderer.ScriptFileName}",
            TestContext.Current.CancellationToken);
        return JsCrewConfigurationAdapter.ToConfiguration(jsCrew);
    }

    [Fact]
    public async Task The_rendered_script_reloads_through_the_script_host_into_the_same_validator()
    {
        var blueprint = Parse(ForgeDocuments.ValidBlueprint);

        var written = ForgeScriptRenderer.Render(blueprint, _sessionDirectory);

        Assert.Equal([Path.Combine("crew", ForgeScriptRenderer.ScriptFileName)], written);
        var source = await File.ReadAllTextAsync(
            Path.Combine(_sessionDirectory, written[0]), TestContext.Current.CancellationToken);
        Assert.StartsWith("/// <reference orkeon-script=\"1.0\" />", source, StringComparison.Ordinal);

        // The real reload: ScriptHost → adapter — §8.3's script column, executed for real.
        var configuration = await ReloadAsync();

        Assert.Equal("veille-fournisseur", configuration.Name);
        Assert.Equal("Résumer les nouvelles offres", configuration.Goal);
        Assert.Equal(Orkeon.Domain.SharedKernel.ValueObjects.ProcessType.Sequential, configuration.Process);
        Assert.Equal(2, configuration.Agents.Count);
        Assert.Equal(2, configuration.Tasks.Count);

        var collecteur = configuration.Agents.Single(a => a.Role == "Web Researcher");
        Assert.Equal(["web_scrape"], collecteur.Tools);

        var resume = configuration.Tasks.Single(t => t.Description == "Rédiger le résumé");
        Assert.Equal(configuration.Agents.Single(a => a.Role == "Writer").Id, resume.AssignedAgentId);
        Assert.Equal([configuration.Tasks.Single(t => t.Description == "Collecter les offres du jour").Id],
            resume.Dependencies);
        Assert.Equal("/output/resume.md", resume.Deliverable?.Path);

        // …into the same validator as the YAML path.
        var verdict = CrewDefinitionValidator.Validate(configuration);
        Assert.True(verdict.IsValid, string.Join("; ", verdict.Errors));
    }

    [Fact]
    public void Kebab_keys_become_prefixed_identifiers_and_the_french_stays_readable()
    {
        var blueprint = Parse(ForgeDocuments.ValidBlueprint
            .Replace("\"collecteur\"", "\"deux-mots\"", StringComparison.Ordinal));

        ForgeScriptRenderer.Render(blueprint, _sessionDirectory);
        var source = File.ReadAllText(Path.Combine(_sessionDirectory, "crew", ForgeScriptRenderer.ScriptFileName));

        Assert.Contains("const agent_deux_mots = agentBuilder()", source, StringComparison.Ordinal);
        Assert.Contains(".agent(agent_deux_mots)", source, StringComparison.Ordinal);
        // Relaxed escaping: the owner reads their own language, not \u sequences.
        Assert.Contains("Résumer les nouvelles offres", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u00e9", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_script_render_replaces_the_yaml_render_entirely()
    {
        var blueprint = Parse(ForgeDocuments.ValidBlueprint);
        ForgeYamlRenderer.Render(ForgeBlueprintCompiler.Compile(blueprint), _sessionDirectory);
        Assert.True(Directory.Exists(Path.Combine(_sessionDirectory, "crew", "agents")));

        ForgeScriptRenderer.Render(blueprint, _sessionDirectory);

        // Same stale-entity policy as YAML: nothing of the previous shape survives.
        Assert.False(Directory.Exists(Path.Combine(_sessionDirectory, "crew", "agents")));
        Assert.Equal([ForgeScriptRenderer.ScriptFileName],
            Directory.GetFiles(Path.Combine(_sessionDirectory, "crew")).Select(Path.GetFileName).ToList());
    }

    [Fact]
    public async Task The_render_stage_picks_the_script_render_from_the_session_format()
    {
        var session = ForgeSession.Create(_sessionDirectory, "veille", format: ForgeSession.FormatScript);
        session.SaveArtifact(ForgeSession.BlueprintFileName, Parse(ForgeDocuments.ValidBlueprint));
        using var output = new StringWriter();

        var outcome = await new RenderStage().RunAsync(
            session, new ForgeEventWriter(output), TestContext.Current.CancellationToken);

        Assert.Equal(ForgeTrigger.Rendered, outcome.Trigger);
        Assert.True(File.Exists(Path.Combine(session.Directory, "crew", ForgeScriptRenderer.ScriptFileName)));
        Assert.Contains("crew.ork.ts", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Tasks_are_declared_in_dependency_order_regardless_of_blueprint_order()
    {
        // 'resume' (depends on 'collecte') listed FIRST: a naive render would reference
        // task_collecte before its const exists.
        var blueprint = Parse("""
            {
              "crew": { "name": "veille", "goal": "Résumer", "process": "sequential" },
              "agents": [ { "key": "solo", "role": "Writer", "goal": "Tout faire" } ],
              "tasks": [
                { "key": "resume", "description": "Rédiger", "expectedOutput": "Le résumé",
                  "agent": "solo", "dependencies": [ "collecte" ] },
                { "key": "collecte", "description": "Collecter", "expectedOutput": "La liste", "agent": "solo" }
              ]
            }
            """);

        ForgeScriptRenderer.Render(blueprint, _sessionDirectory);
        var source = File.ReadAllText(Path.Combine(_sessionDirectory, "crew", ForgeScriptRenderer.ScriptFileName));

        Assert.True(
            source.IndexOf("const task_collecte", StringComparison.Ordinal)
                < source.IndexOf("const task_resume", StringComparison.Ordinal),
            "the dependency must be declared before the task that references it");
        Assert.Contains(".withContext(task_collecte)", source, StringComparison.Ordinal);
    }
}
