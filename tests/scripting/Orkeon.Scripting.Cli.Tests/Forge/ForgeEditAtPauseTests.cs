using System.Text.Json;
using Orkeon.Scripting.Cli.Commands.Forge;
using Orkeon.Scripting.Cli.Tests.Doubles;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>
/// <c>forge resume --edit</c> (remediation v3 W-10): the Composer step shows the proposed
/// agents while the engine sits at the dry pause — amending one is a resume that carries
/// the blueprint over the channel, validated in full, then a deterministic re-render that
/// pauses again at the same boundary. Zero LLM tokens, same iteration. These tests run the
/// real command offline: render and validate never call a model.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class ForgeEditAtPauseTests : IDisposable
{
    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "orkeon-forge-edit-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    /// <summary>A settings file whose Llm section satisfies the host gate; nothing is ever called.</summary>
    private string WriteSettings()
    {
        Directory.CreateDirectory(_workspace);
        var path = Path.Combine(_workspace, "settings.json");
        File.WriteAllText(path, """{ "Llm": { "Model": "never-called", "BaseUrl": "http://127.0.0.1:9" } }""");
        return path;
    }

    /// <summary>A session parked exactly at the dry pause: blueprint saved, state Test, iteration 1.</summary>
    private ForgeSession CreateSessionAtDryPause()
    {
        var session = ForgeSession.Create(_workspace, "veille");
        Assert.True(ForgeBlueprint.TryParse(ForgeDocuments.ValidBlueprint, out var blueprint, out _, []));
        session.SaveArtifact(ForgeSession.BlueprintFileName, blueprint!);
        session.Document.Budget.RegisterIteration();
        session.Document.Iteration = 1;
        session.SetState(ForgeState.Test);
        session.Save(DateTimeOffset.UtcNow);
        return session;
    }

    private static string BlueprintLine(string blueprintJson)
    {
        // The channel reads one JSON document per line: compact the fixture first.
        var compact = JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(blueprintJson));
        return $$"""{"kind":"blueprint.edited","blueprint":{{compact}}}""";
    }

    [Fact]
    public async Task Edit_without_resume_is_refused()
    {
        using var console = new TestConsole();

        var exitCode = await ForgeCommand.DispatchAsync(console, ["--edit"], _workspace);

        Assert.Equal(1, exitCode);
        Assert.Contains("--edit only applies to `forge resume`", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Edit_at_the_dry_pause_rerenders_and_pauses_again_at_the_same_boundary()
    {
        var settings = WriteSettings();
        CreateSessionAtDryPause();
        var amended = ForgeDocuments.ValidBlueprint.Replace("Web Researcher", "Chercheur", StringComparison.Ordinal);
        using var console = new TestConsole(stdin: BlueprintLine(amended) + Environment.NewLine);

        var exitCode = await ForgeCommand.DispatchAsync(console,
            ["resume", "veille", "--edit", "--dry", "--events", "jsonl", "--settings", settings], _workspace);

        Assert.Equal(0, exitCode);
        var stdout = console.Stdout;
        // Exactly one announcement: the command's own, the engine re-run stays silent.
        Assert.Single(stdout.Split('\n'), l => l.Contains("\"kind\":\"session.started\"", StringComparison.Ordinal));
        // The amended blueprint is re-announced with the CURRENT iteration — no charge:
        // this iteration's trial has not run yet.
        var ready = stdout.Split('\n').Single(l => l.Contains("\"kind\":\"blueprint.ready\"", StringComparison.Ordinal));
        Assert.Contains("Chercheur", ready, StringComparison.Ordinal);
        Assert.Contains("\"iteration\":1", ready, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"paused\"", stdout, StringComparison.Ordinal);

        // The session waits at the same boundary, its artifacts amended and re-rendered.
        Assert.True(ForgeSession.TryLoadBySlug(_workspace, "veille", out var reloaded, out _));
        Assert.Equal(ForgeState.Test, reloaded!.State);
        Assert.Equal(1, reloaded.Document.Budget.ConsumedIterations);
        Assert.Equal("Chercheur", reloaded.TryLoadArtifact<ForgeBlueprint>(ForgeSession.BlueprintFileName)!.Agents![0].Role);
        var rendered = await File.ReadAllTextAsync(
            Path.Combine(reloaded.Directory, "crew", "agents", "collecteur.yaml"), TestContext.Current.CancellationToken);
        Assert.Contains("Chercheur", rendered, StringComparison.Ordinal);
        // The coercion is in the history, like any transition the machine makes itself.
        var history = await File.ReadAllTextAsync(
            Path.Combine(reloaded.Directory, ForgeSession.HistoryFileName), TestContext.Current.CancellationToken);
        Assert.Contains("BlueprintEdited", history, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-34")]
    [Trait("BR", "CH-37")]
    public async Task ShouldAcceptTheEditedPlan_WhenTheBriefOfTheSessionListsTheNamedFile()
    {
        var settings = WriteSettings();
        var session = CreateSessionAtDryPause();
        Assert.True(ForgeBrief.TryParse(ForgeDocuments.ValidBrief, out var brief, out _));
        // The fill step filled Contents on the brief; the confirmed folders.json list never carries them.
        session.SaveArtifact(ForgeSession.BriefFileName, brief! with
        {
            Folders = [new ForgeFolder { Path = "/output", Role = "output", Purpose = "The results", Contents = ["template.md"], Existing = ["template.md"] }],
        });
        session.SaveArtifact(ForgeFolders.FileName, new ForgeFolderList
        {
            Folders = [new ForgeFolder { Path = "/output", Role = "output", Purpose = "The results" }],
            Filled = true,
        });
        var amended = ForgeDocuments.ValidBlueprint.Replace(
            "Rédiger le résumé\", \"expectedOutput", "Fill /output/template.md then write the summary\", \"expectedOutput", StringComparison.Ordinal);
        Assert.Contains("Fill /output/template.md", amended, StringComparison.Ordinal);
        using var console = new TestConsole(stdin: BlueprintLine(amended) + Environment.NewLine);

        var exitCode = await ForgeCommand.DispatchAsync(console,
            ["resume", "veille", "--edit", "--dry", "--events", "jsonl", "--settings", settings], _workspace);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("FORGE-BLUEPRINT-INVALID", console.Stdout, StringComparison.Ordinal);
        Assert.True(ForgeSession.TryLoadBySlug(_workspace, "veille", out var reloaded, out _));
        Assert.Contains("template.md", reloaded!.TryLoadArtifact<ForgeBlueprint>(ForgeSession.BlueprintFileName)!.Tasks![1].Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_invalid_edit_leaves_the_session_exactly_at_its_pause()
    {
        var settings = WriteSettings();
        CreateSessionAtDryPause();
        using var console = new TestConsole(
            stdin: """{"kind":"blueprint.edited","blueprint":{"agents":[]}}""" + Environment.NewLine);

        var exitCode = await ForgeCommand.DispatchAsync(console,
            ["resume", "veille", "--edit", "--dry", "--events", "jsonl", "--settings", settings], _workspace);

        Assert.Equal(1, exitCode);
        Assert.Contains("FORGE-BLUEPRINT-INVALID", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"paused\"", console.Stdout, StringComparison.Ordinal);

        // Untouched: same state, same blueprint, nothing rendered.
        Assert.True(ForgeSession.TryLoadBySlug(_workspace, "veille", out var reloaded, out _));
        Assert.Equal(ForgeState.Test, reloaded!.State);
        Assert.Equal("Web Researcher", reloaded.TryLoadArtifact<ForgeBlueprint>(ForgeSession.BlueprintFileName)!.Agents![0].Role);
    }

    /// <summary>
    /// An <c>optional</c> session stopped by its budget between the two passes of its first trial (#431): state Test,
    /// iteration 1, the on pass of the old crew kept under <c>runs/1/on/</c>.
    /// </summary>
    private ForgeSession CreateSessionStoppedBetweenPasses(int maxIterations = ForgeBudget.DefaultMaxIterations)
    {
        var session = ForgeSession.Create(_workspace, "tri", crewDecision: ForgeCrewDecision.Optional);
        Assert.True(ForgeBlueprint.TryParse(ForgeCrewDecisionPlanTests.TriageBlueprint, out var blueprint, out _, [], ForgeCrewDecision.Optional));
        session.SaveArtifact(ForgeSession.BlueprintFileName, blueprint!);
        session.Document.Budget = new ForgeBudget { MaxIterations = maxIterations, MaxTokens = 1_000, ConsumedTokens = 1_500, ConsumedIterations = 1 };
        session.Document.Iteration = 1;
        var pass = new ForgeTestPass { Circuit = "on", Success = true, OutputPath = "runs/1/on/output.md" };
        Directory.CreateDirectory(Path.Combine(session.Directory, TestStage.RunsDirectoryName, "1", "on"));
        session.SaveArtifact(Path.Combine(TestStage.RunsDirectoryName, "1", "on", "run.json"),
            new ForgeTestRun { Success = true, Output = "Tri par l'ancien crew.", Circuit = "on", Passes = [pass] });
        session.SetState(ForgeState.Test);
        session.SetStatus(ForgeSessionStatus.BudgetExhausted);
        session.Save(DateTimeOffset.UtcNow);
        return session;
    }

    [Fact]
    [Trait("BR", "CH-431")]
    public async Task An_edit_after_a_budget_stop_between_passes_starts_a_new_iteration_whose_trial_runs_both_passes()
    {
        // Arrange
        var settings = WriteSettings();
        CreateSessionStoppedBetweenPasses();
        var amended = ForgeCrewDecisionPlanTests.TriageBlueprint.Replace("\"Reader\"", "\"Lecteur\"", StringComparison.Ordinal);
        using var console = new TestConsole(stdin: BlueprintLine(amended) + Environment.NewLine);

        // Act
        var exitCode = await ForgeCommand.DispatchAsync(console,
            ["resume", "tri", "--edit", "--dry", "--max-tokens", "100000", "--events", "jsonl", "--settings", settings], _workspace);

        // Assert: the edited crew is a new cycle, so the old crew's on pass is no pass of its trial.
        Assert.Equal(0, exitCode);
        var ready = console.Stdout.Split('\n').Single(l => l.Contains("\"kind\":\"blueprint.ready\"", StringComparison.Ordinal));
        Assert.Contains("\"iteration\":2", ready, StringComparison.Ordinal);
        Assert.True(ForgeSession.TryLoadBySlug(_workspace, "tri", out var reloaded, out _));
        Assert.Equal(ForgeState.Test, reloaded!.State);
        Assert.Equal(2, reloaded.Document.Budget.ConsumedIterations);
        Assert.Equal(2, reloaded.Document.Iteration);
        var bench = new FakeCircuitRecordingTestBench();
        using var output = new StringWriter();
        await new TestStage(bench, new ForgeRunObserver(), Orkeon.Application.Crew.Execution.DecisionCircuit.On)
            .RunAsync(reloaded, new ForgeEventWriter(output), TestContext.Current.CancellationToken);
        Assert.Equal(["on", "off"], bench.Circuits.Select(c => ForgeCrewDecision.CircuitName(c!)));
    }

    [Fact]
    [Trait("BR", "CH-431")]
    public async Task An_edit_after_a_budget_stop_between_passes_with_no_iteration_left_is_refused_and_saves_nothing()
    {
        // Arrange
        var settings = WriteSettings();
        CreateSessionStoppedBetweenPasses(maxIterations: 1);
        var amended = ForgeCrewDecisionPlanTests.TriageBlueprint.Replace("\"Reader\"", "\"Lecteur\"", StringComparison.Ordinal);
        using var console = new TestConsole(stdin: BlueprintLine(amended) + Environment.NewLine);

        // Act
        var exitCode = await ForgeCommand.DispatchAsync(console,
            ["resume", "tri", "--edit", "--dry", "--max-tokens", "100000", "--events", "jsonl", "--settings", settings], _workspace);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Contains(ForgeEngine.CodeBudgetExhausted, console.Stdout, StringComparison.Ordinal);
        Assert.True(ForgeSession.TryLoadBySlug(_workspace, "tri", out var reloaded, out _));
        Assert.Equal(ForgeState.Test, reloaded!.State);
        Assert.Equal(1, reloaded.Document.Budget.ConsumedIterations);
        Assert.Equal("Reader", reloaded.TryLoadArtifact<ForgeBlueprint>(ForgeSession.BlueprintFileName)!.Agents![0].Role);
    }

    [Fact]
    public async Task Edit_away_from_the_pause_points_at_the_arbitrations_own_decision()
    {
        var settings = WriteSettings();
        var session = CreateSessionAtDryPause();
        session.SetState(ForgeState.Verdict);
        session.Save(DateTimeOffset.UtcNow);
        using var console = new TestConsole();

        var exitCode = await ForgeCommand.DispatchAsync(console,
            ["resume", "veille", "--edit", "--events", "jsonl", "--settings", settings], _workspace);

        Assert.Equal(1, exitCode);
        Assert.Contains("use the edit decision instead", console.Stderr, StringComparison.Ordinal);
    }
}
