using System.Text.Json;
using Orkeon.Scripting.Cli.Commands.Forge;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>
/// <c>--crew-decision</c> at the command line (#431): the option is recorded by a new session, refused when it would
/// change on resume, derived for a session older than it, and a required crew is refused its trial without System One.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class ForgeCrewDecisionCommandTests : IDisposable
{
    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "orkeon-forge-crew-decision-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    /// <summary>A settings file whose Llm section satisfies the host gate, with no decision model; nothing is called.</summary>
    private string WriteSettings()
    {
        Directory.CreateDirectory(_workspace);
        var path = Path.Combine(_workspace, "settings.json");
        File.WriteAllText(path, """{ "Llm": { "Model": "never-called", "BaseUrl": "http://127.0.0.1:9" } }""");
        return path;
    }

    /// <summary>A session parked at the dry pause with the triage plan, in <paramref name="mode"/>.</summary>
    private ForgeSession SessionAtDryPause(string? mode)
    {
        var session = ForgeSession.Create(_workspace, "tri", crewDecision: mode ?? ForgeCrewDecision.None);
        Assert.True(ForgeBlueprint.TryParse(ForgeCrewDecisionPlanTests.TriageBlueprint, out var blueprint, out _, [], ForgeCrewDecision.Required));
        session.SaveArtifact(ForgeSession.BlueprintFileName, blueprint!);
        ForgeYamlRenderer.Render(ForgeBlueprintCompiler.Compile(blueprint!, crewDecision: mode ?? ForgeCrewDecision.Required), session.Directory);
        session.Document.Budget.RegisterIteration();
        session.Document.Iteration = 1;
        session.Document.CrewDecision = mode;
        session.SetState(ForgeState.Test);
        session.Save(DateTimeOffset.UtcNow);
        return session;
    }

    [Theory]
    [InlineData("none")]
    [InlineData("optional")]
    [InlineData("Required")]
    public void The_option_reads_each_mode(string mode)
    {
        var options = ForgeCommandOptions.Parse(["une", "veille", "--crew-decision", mode]);

        Assert.Null(options.Error);
    }

    [Fact]
    public async Task An_unknown_mode_is_refused_with_the_three_real_ones()
    {
        using var console = new TestConsole();

        var exitCode = await ForgeCommand.DispatchAsync(console, ["un", "besoin", "--crew-decision", "maybe"], _workspace);

        Assert.Equal(1, exitCode);
        Assert.Contains("unknown crew decision mode 'maybe' — use none, optional or required", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_session_records_its_mode_in_session_json()
    {
        var session = ForgeSession.Create(_workspace, "tri", crewDecision: ForgeCrewDecision.Optional);

        var document = JsonElement.Parse(File.ReadAllText(Path.Combine(session.Directory, "session.json")));

        Assert.Equal("optional", document.GetProperty("crewDecision").GetString());
    }

    [Fact]
    public async Task A_resume_cannot_change_the_crew_decision_mode()
    {
        SessionAtDryPause(ForgeCrewDecision.Optional);
        using var console = new TestConsole();

        var exitCode = await ForgeCommand.DispatchAsync(console, ["resume", "tri", "--crew-decision", "required"], _workspace);

        Assert.Equal(1, exitCode);
        Assert.Contains("was created with --crew-decision optional", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_legacy_session_derives_its_mode_from_its_plan_and_warns()
    {
        var settings = WriteSettings();
        var session = SessionAtDryPause(mode: null);
        using var console = new TestConsole();

        await ForgeCommand.DispatchAsync(console, ["resume", "tri", "--dry", "--events", "jsonl", "--settings", settings], _workspace);

        Assert.Contains(ForgeCrewDecision.DerivedWarning, console.Stdout, StringComparison.Ordinal);
        var document = JsonElement.Parse(await File.ReadAllTextAsync(Path.Combine(session.Directory, "session.json"), TestContext.Current.CancellationToken));
        Assert.Equal("required", document.GetProperty("crewDecision").GetString());
    }

    [Fact]
    public async Task A_required_crew_is_refused_its_trial_when_no_decision_model_is_configured()
    {
        var settings = WriteSettings();
        SessionAtDryPause(ForgeCrewDecision.Required);
        using var console = new TestConsole();

        var exitCode = await ForgeCommand.DispatchAsync(console, ["resume", "tri", "--settings", settings], _workspace);

        Assert.Equal(1, exitCode);
        Assert.Contains(ForgeCrewDecision.UnavailableCode, console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_required_crew_refusal_is_on_the_stream_with_its_cause()
    {
        var settings = WriteSettings();
        SessionAtDryPause(ForgeCrewDecision.Required);
        using var console = new TestConsole();

        await ForgeCommand.DispatchAsync(console, ["resume", "tri", "--events", "jsonl", "--settings", settings], _workspace);

        var error = console.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonElement.Parse(line))
            .Single(e => e.GetProperty("kind").GetString() == "error");
        Assert.Equal(ForgeCrewDecision.UnavailableCode, error.GetProperty("code").GetString());
        Assert.Equal("no-provider", error.GetProperty("cause").GetString());
    }

    [Fact]
    public async Task A_new_required_session_is_refused_before_its_interview_without_system_one()
    {
        var settings = WriteSettings();
        using var console = new TestConsole();

        var exitCode = await ForgeCommand.DispatchAsync(console, ["trier", "les", "demandes", "--crew-decision", "required", "--settings", settings], _workspace);

        Assert.Equal(1, exitCode);
        Assert.Contains(ForgeCrewDecision.UnavailableCode, console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Adopting_a_required_crew_without_system_one_is_not_refused()
    {
        SessionAtDryPause(ForgeCrewDecision.Required);
        using var console = new TestConsole();

        var exitCode = await ForgeCommand.DispatchAsync(console, ["resume", "tri", "--adopt"], _workspace);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task A_dry_run_of_a_required_crew_without_system_one_only_warns()
    {
        var settings = WriteSettings();
        SessionAtDryPause(ForgeCrewDecision.Required);
        using var console = new TestConsole();

        var exitCode = await ForgeCommand.DispatchAsync(console, ["resume", "tri", "--dry", "--events", "jsonl", "--settings", settings], _workspace);

        Assert.Equal(0, exitCode);
        Assert.Contains(ForgeCrewDecision.UnavailableCode, console.Stdout, StringComparison.Ordinal);
    }
}
