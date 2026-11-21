using System.Text.Json;
using Orkeon.Scripting.Cli.Commands.Forge;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>
/// The crew decision mode beyond the session (#431): a promoted team records it in <c>forge.json</c> and states it
/// in <c>FORGE.md</c> and its launcher, and a reopened team gets its decisions, conditions, relays and mode back.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class ForgeCrewDecisionTeamTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 10, 0, 0, TimeSpan.Zero);

    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "orkeon-forge-team-decision-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    private string Destination => Path.Combine(_workspace, "teams", "tri");

    private string CrewDirectory => Path.Combine(_workspace, "render");

    /// <summary>The triage plan in <paramref name="mode"/>; for <c>none</c>, which allows no decision, the plain plan.</summary>
    private static ForgeBlueprint Triage(string mode)
    {
        var json = mode == ForgeCrewDecision.None ? ForgeDocuments.ValidBlueprint : ForgeCrewDecisionPlanTests.TriageBlueprint;
        Assert.True(ForgeBlueprint.TryParse(json, out var blueprint, out var errors, [], mode), string.Join("; ", errors));
        return blueprint!;
    }

    /// <summary>Renders the triage crew in <paramref name="mode"/> and returns its crew folder.</summary>
    private string RenderTriage(string mode)
    {
        ForgeYamlRenderer.Render(ForgeBlueprintCompiler.Compile(Triage(mode), crewDecision: mode), CrewDirectory);
        return Path.Combine(CrewDirectory, ForgeYamlRenderer.CrewDirectoryName);
    }

    private ForgeSession ReadySession(string mode)
    {
        var session = ForgeSession.Create(_workspace, "tri", crewDecision: mode);
        session.Document.Title = "Trier les demandes entrantes";
        Assert.True(ForgeBrief.TryParse(ForgeDocuments.ValidBrief, out var brief, out _));
        session.SaveArtifact(ForgeSession.BriefFileName, brief!);
        session.SaveArtifact("verdict.json", new ForgeVerdict { Score = 0.9, Passing = true, Judge = ForgeVerdict.JudgeLlm });
        ForgeYamlRenderer.Render(ForgeBlueprintCompiler.Compile(Triage(mode), crewDecision: mode), session.Directory);
        session.SetState(ForgeState.Ready);
        session.SetStatus(ForgeSessionStatus.Ready);
        session.Save(Now);
        return session;
    }

    private ForgeSession Promote(string mode)
    {
        var session = ReadySession(mode);
        ForgePromoter.Promote(
            session, Destination, schedule: null, settingsPath: null, copySettings: false,
            ForgePromotePlatform.Linux, Now);
        return session;
    }

    /// <summary>Removes the crew-level <c>decision:</c> block from the crew under <paramref name="crew"/>.</summary>
    private static void StripDecisionBlock(string crew)
    {
        var config = Path.Combine(crew, "config.yaml");
        File.WriteAllLines(config, File.ReadAllLines(config).Where(line => !line.StartsWith("decision", StringComparison.Ordinal) && !line.StartsWith("  mode", StringComparison.Ordinal)));
    }

    /// <summary>
    /// A promoted team in <paramref name="mode"/> whose session is gone, so a reopen rebuilds one from the folder;
    /// <paramref name="declaresMode"/> false strips its <c>decision:</c> block first.
    /// </summary>
    private void PromotedTeamWithoutItsSession(string mode, bool declaresMode)
    {
        var session = Promote(mode);
        Directory.Delete(session.Directory, recursive: true);
        if (!declaresMode)
            StripDecisionBlock(Path.Combine(Destination, ForgeYamlRenderer.CrewDirectoryName));
    }

    private static IReadOnlyList<JsonElement> Events(string stdout) =>
    [
        .. stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonElement.Parse(line)),
    ];

    [Fact]
    public async Task A_promoted_team_records_its_mode_in_forge_json()
    {
        Promote(ForgeCrewDecision.Optional);

        var record = JsonElement.Parse(await File.ReadAllTextAsync(Path.Combine(Destination, ForgeTeamRecord.FileName), TestContext.Current.CancellationToken));

        Assert.Equal("optional", record.GetProperty("crewDecision").GetString());
    }

    [Fact]
    public async Task A_promoted_required_team_states_its_mode_in_its_card()
    {
        Promote(ForgeCrewDecision.Required);

        var card = await File.ReadAllTextAsync(Path.Combine(Destination, ForgePromoter.CardFileName), TestContext.Current.CancellationToken);

        Assert.Contains("Mode de décision : required", card, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_promoted_optional_team_states_its_mode_under_the_launcher_header()
    {
        Promote(ForgeCrewDecision.Optional);

        var lines = await File.ReadAllLinesAsync(Path.Combine(Destination, "run.sh"), TestContext.Current.CancellationToken);

        var header = Array.FindIndex(lines, line => line.StartsWith("# ", StringComparison.Ordinal));
        Assert.StartsWith("# Decision mode: optional.", lines[header + 1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_promoted_none_team_states_no_mode()
    {
        Promote(ForgeCrewDecision.None);

        var launcher = await File.ReadAllTextAsync(Path.Combine(Destination, "run.sh"), TestContext.Current.CancellationToken);

        Assert.DoesNotContain("Decision mode", launcher, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reopened_optional_team_gets_its_mode_back()
    {
        var crew = RenderTriage(ForgeCrewDecision.Optional);

        var read = ForgeTeamReader.TryRead(crew, out _, out var errors, [], out var mode);

        Assert.True(read, string.Join("; ", errors));
        Assert.Equal(ForgeCrewDecision.Optional, mode);
    }

    [Fact]
    public void A_reopened_team_gets_its_decision_its_relay_and_its_condition_back()
    {
        var crew = RenderTriage(ForgeCrewDecision.Optional);

        var read = ForgeTeamReader.TryRead(crew, out var blueprint, out var errors, [], out _);

        Assert.True(read, string.Join("; ", errors));
        var triage = blueprint!.Tasks!.Single(t => t.Key == "triage");
        Assert.Equal("relais", triage.Decision!.Without!.Agent);
        Assert.Equal(["bug", "feature"], triage.Decision.Questions!["category"].Choice!.Keys.Order(StringComparer.Ordinal));
        Assert.True(blueprint.Tasks!.Single(t => t.Key == "correction").When!.ContainsKey("triage.category"));
    }

    [Fact]
    public void A_reopened_team_without_a_mode_derives_it_from_its_decision_tasks()
    {
        var crew = RenderTriage(ForgeCrewDecision.Required);
        StripDecisionBlock(crew);

        var read = ForgeTeamReader.TryRead(crew, out _, out var errors, [], out var mode);

        Assert.True(read, string.Join("; ", errors));
        Assert.Equal(ForgeCrewDecision.Required, mode);
    }

    [Fact]
    public async Task A_reopened_team_without_a_mode_is_warned_after_session_started_that_its_plan_makes_it_required()
    {
        PromotedTeamWithoutItsSession(ForgeCrewDecision.Required, declaresMode: false);
        using var console = new TestConsole();

        var exitCode = await ForgeCommand.DispatchAsync(console, ["reopen", Destination, "--events", "jsonl"], _workspace);

        Assert.Equal(0, exitCode);
        var events = Events(console.Stdout);
        Assert.Equal(["session.started", "warning"], events.Take(2).Select(e => e.GetProperty("kind").GetString()));
        Assert.Equal(ForgeCrewDecision.DerivedWarning, events[1].GetProperty("code").GetString());
        Assert.Contains("mode: required", events[1].GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-431")]
    public async Task A_reopened_team_without_a_mode_is_warned_of_its_derived_mode_once()
    {
        // Arrange
        PromotedTeamWithoutItsSession(ForgeCrewDecision.Required, declaresMode: false);
        using var console = new TestConsole();

        // Act
        await ForgeCommand.DispatchAsync(console, ["reopen", Destination, "--events", "jsonl"], _workspace);

        // Assert
        Assert.Single(Events(console.Stdout), e => e.GetProperty("kind").GetString() == "warning"
            && e.GetProperty("code").GetString() == ForgeCrewDecision.DerivedWarning);
    }

    [Fact]
    public async Task A_reopened_team_that_declares_its_mode_is_not_warned()
    {
        PromotedTeamWithoutItsSession(ForgeCrewDecision.Required, declaresMode: true);
        using var console = new TestConsole();

        await ForgeCommand.DispatchAsync(console, ["reopen", Destination, "--events", "jsonl"], _workspace);

        Assert.DoesNotContain(ForgeCrewDecision.DerivedWarning, console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void The_session_started_line_carries_the_crew_decision_mode()
    {
        var session = ForgeSession.Create(_workspace, "tri", crewDecision: ForgeCrewDecision.Required);
        using var output = new StringWriter();

        new ForgeEventWriter(output, new FakeOrkeonClock()).SessionStarted(session, resumed: false);

        Assert.Equal("required", JsonElement.Parse(output.ToString().Trim()).GetProperty("crewDecision").GetString());
    }
}
