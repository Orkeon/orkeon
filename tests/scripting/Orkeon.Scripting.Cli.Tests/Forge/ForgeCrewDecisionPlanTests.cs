using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Crew.Execution;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.Crew;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Scripting;
using Orkeon.Scripting.Adapters;
using Orkeon.Scripting.Cli.Commands.Forge;
using Orkeon.Scripting.Toolchain;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>
/// The crew decision mode of a plan (#431, <c>--crew-decision</c>): what each mode lets a plan hold, and that the
/// crew it renders, in YAML or in script, loads through the engine's own loader with the declared mode.
/// </summary>
public sealed class ForgeCrewDecisionPlanTests : IDisposable
{
    private readonly string _sessionDirectory =
        Path.Combine(Path.GetTempPath(), "orkeon-forge-decision-" + Guid.NewGuid().ToString("N"));

    public ForgeCrewDecisionPlanTests() => Directory.CreateDirectory(_sessionDirectory);

    public void Dispose()
    {
        if (Directory.Exists(_sessionDirectory))
            Directory.Delete(_sessionDirectory, recursive: true);
    }

    /// <summary>A triage crew: a decision task with no agent and no expected output, a relay agent that runs no task.</summary>
    internal const string TriageBlueprint = """
        {
          "crew": { "name": "tri-demandes", "goal": "Trier les demandes entrantes", "process": "sequential" },
          "agents": [
            { "key": "lecteur", "role": "Reader", "goal": "Lire la demande" },
            { "key": "relais", "role": "Triage relay", "goal": "Repondre aux questions de tri" },
            { "key": "correcteur", "role": "Fixer", "goal": "Proposer un correctif" }
          ],
          "tasks": [
            { "key": "lecture", "description": "Lire la demande", "expectedOutput": "La demande resumee", "agent": "lecteur" },
            { "key": "triage", "description": "Classer la demande", "dependencies": [ "lecture" ],
              "decision": { "questions": { "category": { "choice": { "bug": "un defaut", "feature": "une demande" } } },
                            "without": { "agent": "relais" } } },
            { "key": "correction", "description": "Proposer un correctif", "expectedOutput": "Le correctif",
              "agent": "correcteur", "dependencies": [ "triage" ], "when": { "triage.category": [ "bug" ] } }
          ],
          "rationale": "Lire, trier, corriger seulement les defauts."
        }
        """;

    private static ForgeBlueprint Parse(string json, string mode)
    {
        Assert.True(ForgeBlueprint.TryParse(json, out var blueprint, out var errors, [], mode), string.Join("; ", errors));
        return blueprint!;
    }

    private static IReadOnlyList<string> Errors(string json, string mode)
    {
        var parsed = ForgeBlueprint.TryParse(json, out _, out var errors, [], mode);
        Assert.Equal(parsed, errors.Count == 0);
        return errors;
    }

    private async Task<CrewConfiguration> LoadYamlAsync()
    {
        var loader = new YamlCrewDefinitionLoader(
            new YamlDotNetSerializer(),
            new DiskBackedFileSystemService(_sessionDirectory, "/forge"),
            NullLogger<YamlCrewDefinitionLoader>.Instance);
        return await loader.LoadFromDirectoryAsync("/forge/crew", TestContext.Current.CancellationToken);
    }

    private async Task<CrewConfiguration> LoadScriptAsync()
    {
        var scriptHost = new ScriptHost(
            new DiskBackedFileSystemService(_sessionDirectory, "/forge"),
            PassThroughTranspiler.Instance,
            new JsEngineFactory(loggerFactory: NullLoggerFactory.Instance, builtInTools: []),
            NullLogger<ScriptHost>.Instance);
        var jsCrew = await scriptHost.LoadCrewFromFileAsync(
            Path.Combine(_sessionDirectory, "crew", ForgeScriptRenderer.ScriptFileName),
            $"/forge/crew/{ForgeScriptRenderer.ScriptFileName}",
            TestContext.Current.CancellationToken);
        return JsCrewConfigurationAdapter.ToConfiguration(jsCrew);
    }

    private static void AssertValid(CrewConfiguration configuration)
    {
        var verdict = CrewDefinitionValidator.Validate(configuration);
        Assert.True(verdict.IsValid, string.Join("; ", verdict.Errors));
    }

    [Fact]
    public async Task A_none_crew_renders_no_decision_block_and_loads()
    {
        var blueprint = Parse(ForgeDocuments.ValidBlueprint, ForgeCrewDecision.None);

        ForgeYamlRenderer.Render(ForgeBlueprintCompiler.Compile(blueprint, crewDecision: ForgeCrewDecision.None), _sessionDirectory);

        Assert.DoesNotContain("decision", await File.ReadAllTextAsync(Path.Combine(_sessionDirectory, "crew", "config.yaml"), TestContext.Current.CancellationToken), StringComparison.Ordinal);
        var configuration = await LoadYamlAsync();
        Assert.Equal(CrewDecisionMode.Unspecified, configuration.DecisionMode);
        AssertValid(configuration);
    }

    [Fact]
    public async Task A_required_crew_renders_its_mode_and_loads_its_decision_task()
    {
        var blueprint = Parse(TriageBlueprint, ForgeCrewDecision.Required);

        ForgeYamlRenderer.Render(ForgeBlueprintCompiler.Compile(blueprint, crewDecision: ForgeCrewDecision.Required), _sessionDirectory);

        var configuration = await LoadYamlAsync();
        Assert.Equal(CrewDecisionMode.Required, configuration.DecisionMode);
        Assert.NotNull(configuration.Tasks.Single(t => t.Key == "triage").Decision);
        AssertValid(configuration);
    }

    [Fact]
    public async Task An_optional_crew_renders_its_mode_and_its_relay_and_loads()
    {
        var blueprint = Parse(TriageBlueprint, ForgeCrewDecision.Optional);

        ForgeYamlRenderer.Render(ForgeBlueprintCompiler.Compile(blueprint, crewDecision: ForgeCrewDecision.Optional), _sessionDirectory);

        var configuration = await LoadYamlAsync();
        Assert.Equal(CrewDecisionMode.Optional, configuration.DecisionMode);
        Assert.NotNull(configuration.Tasks.Single(t => t.Key == "triage").Decision!.Without);
        AssertValid(configuration);
    }

    [Fact]
    public async Task A_required_crew_rendered_as_a_script_loads_with_its_mode()
    {
        var blueprint = Parse(TriageBlueprint, ForgeCrewDecision.Required);

        ForgeScriptRenderer.Render(blueprint, _sessionDirectory, ForgeCrewDecision.Required);

        var configuration = await LoadScriptAsync();
        Assert.Equal(CrewDecisionMode.Required, configuration.DecisionMode);
        Assert.NotNull(configuration.Tasks.Single(t => t.Key == "triage").Decision);
        AssertValid(configuration);
    }

    [Fact]
    public async Task An_optional_crew_rendered_as_a_script_loads_with_its_relay()
    {
        var blueprint = Parse(TriageBlueprint, ForgeCrewDecision.Optional);

        ForgeScriptRenderer.Render(blueprint, _sessionDirectory, ForgeCrewDecision.Optional);

        var configuration = await LoadScriptAsync();
        Assert.Equal(CrewDecisionMode.Optional, configuration.DecisionMode);
        Assert.NotNull(configuration.Tasks.Single(t => t.Key == "triage").Decision!.Without);
    }

    [Fact]
    public void A_decision_task_with_no_agent_and_a_relay_agent_with_no_task_validate()
    {
        var errors = Errors(TriageBlueprint, ForgeCrewDecision.Optional);

        Assert.Empty(errors);
    }

    [Fact]
    public void A_decision_task_with_no_expected_output_passes_the_shared_validator()
    {
        var compilation = ForgeBlueprintCompiler.Compile(Parse(TriageBlueprint, ForgeCrewDecision.Optional), crewDecision: ForgeCrewDecision.Optional);

        var verdict = ForgeBlueprintCompiler.Validate(compilation, ForgeDocuments.KnownTools);

        Assert.Empty(verdict.Errors);
    }

    [Fact]
    public void A_none_session_refuses_a_plan_with_a_decision_and_a_condition()
    {
        var errors = Errors(TriageBlueprint, ForgeCrewDecision.None);

        Assert.Contains(errors, e => e.Contains("'triage'", StringComparison.Ordinal) && e.Contains("'decision'", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("'correction'", StringComparison.Ordinal) && e.Contains("'when'", StringComparison.Ordinal));
    }

    [Fact]
    public void A_required_session_refuses_a_plan_that_asks_system_one_nothing()
    {
        var errors = Errors(ForgeDocuments.ValidBlueprint, ForgeCrewDecision.Required);

        Assert.Contains(errors, e => e.Contains("'required'", StringComparison.Ordinal));
    }

    [Fact]
    public void An_optional_session_refuses_a_decision_task_without_its_relay()
    {
        var json = TriageBlueprint.Replace("\"without\": { \"agent\": \"relais\" }", "\"minConfidence\": 0.5", StringComparison.Ordinal);

        var errors = Errors(json, ForgeCrewDecision.Optional);

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void A_decision_task_that_names_an_agent_is_refused_with_the_engine_message()
    {
        var json = TriageBlueprint.Replace("\"key\": \"triage\",", "\"key\": \"triage\", \"agent\": \"lecteur\",", StringComparison.Ordinal);

        var errors = Errors(json, ForgeCrewDecision.Required);

        Assert.Contains(errors, e => e.Contains("'triage'", StringComparison.Ordinal) && e.Contains("decision", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(ForgeCrewDecision.None, true, false)]
    [InlineData(ForgeCrewDecision.Optional, true, false)]
    [InlineData(ForgeCrewDecision.Required, true, true)]
    [InlineData(ForgeCrewDecision.Required, false, false)]
    public void The_designer_is_offered_system_one_decide_in_a_required_crew_with_the_circuit_on(string mode, bool circuitOn, bool offered)
    {
        var circuit = circuitOn ? DecisionCircuit.On : DecisionCircuit.OffBy(DecisionCircuitCause.Switch);
        var tools = new[] { "file_read", ForgeCrewDecision.DecideTool }
            .Select(name => new Orkeon.Tests.Shared.Doubles.StubBaseTool(name))
            .ToList();

        var names = ForgeSandbox.SelectCrewTools(tools, mode, circuit).Select(t => t.Name).ToList();

        Assert.Equal(offered, names.Contains(ForgeCrewDecision.DecideTool));
    }

    [Theory]
    [InlineData("""{ "tasks": [ { "key": "t", "decision": { "questions": {} } } ] }""", ForgeCrewDecision.Required)]
    [InlineData("""{ "agents": [ { "key": "a", "tools": [ "system_one_decide" ] } ] }""", ForgeCrewDecision.Required)]
    [InlineData("""{ "tasks": [ { "key": "t", "tools": [ "system_one_decide" ] } ] }""", ForgeCrewDecision.Required)]
    [InlineData("""{ "tasks": [ { "key": "t" } ] }""", ForgeCrewDecision.None)]
    public void A_legacy_plan_derives_its_mode_from_what_it_holds(string json, string expected)
    {
        var blueprint = System.Text.Json.JsonSerializer.Deserialize<ForgeBlueprint>(json);

        var mode = ForgeCrewDecision.Derive(blueprint);

        Assert.Equal(expected, mode);
    }

    [Theory]
    [InlineData(ForgeCrewDecision.None, true, new[] { "on" })]
    [InlineData(ForgeCrewDecision.Required, true, new[] { "on" })]
    [InlineData(ForgeCrewDecision.Optional, true, new[] { "on", "off" })]
    [InlineData(ForgeCrewDecision.Optional, false, new[] { "off" })]
    public void A_trial_runs_one_pass_per_circuit_the_mode_needs(string mode, bool hostOn, string[] expected)
    {
        var host = hostOn ? DecisionCircuit.On : DecisionCircuit.OffBy(DecisionCircuitCause.NoProvider);

        var passes = ForgeCrewDecision.PassesOf(mode, host).Select(ForgeCrewDecision.CircuitName).ToArray();

        Assert.Equal(expected, passes);
    }
}
