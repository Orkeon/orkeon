using Orkeon.Scripting.Cli.Commands.Forge;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>
/// The <c>forge</c> dispatch as this slice ships it: <c>list</c> is complete, everything
/// else refuses honestly — and the verb stays out of <c>Program.cs</c> until the stages
/// can hold its promise (FORGE-02's recorded re-scope).
/// </summary>
[Collection(CliCollection.Name)]
public sealed class ForgeCommandTests : IDisposable
{
    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "orkeon-forge-cmd-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    [Fact]
    public async Task List_on_an_empty_workspace_says_so_and_exits_zero()
    {
        using var console = new TestConsole();

        var exitCode = await ForgeCommand.DispatchAsync(["list"], _workspace);

        Assert.Equal(0, exitCode);
        Assert.Contains("No forge session", console.Stdout, StringComparison.Ordinal);
        Assert.Contains(ForgeSession.RootFor(_workspace), console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_prints_the_sessions_most_recent_first_in_wire_spelling()
    {
        var older = ForgeSession.Create(_workspace, "older");
        older.SetState(ForgeState.Verdict);
        older.Save(new DateTimeOffset(2026, 8, 19, 10, 0, 0, TimeSpan.Zero));
        var newer = ForgeSession.Create(_workspace, "newer");
        newer.Save(new DateTimeOffset(2026, 8, 19, 11, 0, 0, TimeSpan.Zero));

        using var console = new TestConsole();
        var exitCode = await ForgeCommand.DispatchAsync(["list"], _workspace);

        Assert.Equal(0, exitCode);
        var lines = console.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("SLUG", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("newer", lines[1], StringComparison.Ordinal);
        Assert.StartsWith("older", lines[2], StringComparison.Ordinal);
        Assert.Contains("verdict", lines[2], StringComparison.Ordinal);   // wire spelling, not 'Verdict'
    }

    [Fact]
    public async Task An_unknown_option_is_refused_loudly()
    {
        using var console = new TestConsole();

        var exitCode = await ForgeCommand.DispatchAsync(["--bogus"], _workspace);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown option '--bogus'", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_format_is_refused_with_the_two_real_ones()
    {
        using var console = new TestConsole();

        var exitCode = await ForgeCommand.DispatchAsync(["un", "besoin", "--format", "toml"], _workspace);

        Assert.Equal(1, exitCode);
        Assert.Contains("unknown format 'toml' — use yaml or script", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_resume_cannot_change_the_session_format()
    {
        ForgeSession.Create(_workspace, "veille", format: ForgeSession.FormatYaml);
        using var console = new TestConsole();

        var exitCode = await ForgeCommand.DispatchAsync(["resume", "veille", "--format", "script"], _workspace);

        Assert.Equal(1, exitCode);
        Assert.Contains("cannot change on resume", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resuming_a_ghost_session_names_the_root_and_exits_one()
    {
        using var console = new TestConsole();

        var exitCode = await ForgeCommand.DispatchAsync(["resume", "ghost"], _workspace);

        Assert.Equal(1, exitCode);
        Assert.Contains("ghost", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_an_llm_the_command_refuses_with_the_remedy_before_any_turn()
    {
        // The assembly-level guard pins the global settings path to a nonexistent file, so
        // the resolution chain finds nothing here: the interview must refuse honestly.
        using var console = new TestConsole();

        var exitCode = await ForgeCommand.DispatchAsync(["un", "besoin", "concret"], _workspace);

        Assert.Equal(1, exitCode);
        Assert.Contains("FORGE-LLM-UNAVAILABLE", console.Stderr, StringComparison.Ordinal);
        Assert.Contains("orkeon init", console.Stderr, StringComparison.Ordinal);

        // The session was created before the refusal — resumable once the LLM exists.
        var session = Assert.Single(ForgeSession.List(_workspace));
        Assert.Equal("un-besoin-concret", session.Slug);
    }

    /// <summary>
    /// A settings file that claims one of the three roots forge mounts for itself is a
    /// configuration mistake, and has to read as one.
    /// <para>
    /// Forge accepts no <c>--mount</c>, so its settings file is the ONLY place its roots can
    /// be claimed from — and that was exactly the case the reserved-root guard never covered:
    /// every other entry point calls it, on its command-line mounts. The team composer writes
    /// <c>/output</c> into the settings the moment a team produces a deliverable, so creating
    /// a team and then composing it was enough to reach it, and what came back was
    /// "Duplicate virtual paths: /output" thrown out of a DI factory.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_settings_declared_output_mount_is_refused_rather_than_crashing_the_host_build()
    {
        Directory.CreateDirectory(_workspace);
        var deliverables = Path.Combine(_workspace, "livrables");
        Directory.CreateDirectory(deliverables);
        var claim = System.Text.Json.JsonSerializer.Serialize(
            Orkeon.Domain.FileSystem.FileSystemMount.Quote(deliverables) + ":/output:rw");
        await File.WriteAllTextAsync(
            Path.Combine(_workspace, "appsettings.json"),
            "{ \"Llm\": { \"Provider\": \"ollama\", \"Model\": \"llama3.2\" },"
            + " \"Orkeon\": { \"FileSystem\": { \"Mounts\": [ " + claim + " ] } } }",
            TestContext.Current.CancellationToken);

        using var console = new TestConsole();

        var exitCode = await ForgeCommand.DispatchAsync([], _workspace);

        Assert.Equal(1, exitCode);
        Assert.Contains("/output", console.Stderr, StringComparison.Ordinal);
        Assert.Contains("reserved", console.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("Duplicate virtual paths", console.Stderr, StringComparison.Ordinal);
    }
    /// <summary>
    /// <c>resume --adopt</c> end to end: the dry pause goes to Ready, offline, and the
    /// stream closes on «ready» so a client knows the wizard may save the team.
    /// </summary>
    [Fact]
    public async Task Adopt_takes_the_dry_pause_straight_to_ready()
    {
        var session = ForgeSession.Create(_workspace, "demo");
        session.SetState(ForgeState.Test);
        session.Save();

        using var console = new TestConsole();
        var exitCode = await ForgeCommand.DispatchAsync(["resume", "demo", "--adopt", "--events", "jsonl"], _workspace);

        Assert.Equal(0, exitCode);
        var kinds = console.Stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => System.Text.Json.JsonElement.Parse(line))
            .ToList();
        Assert.Equal(["session.started", "session.finished"], kinds.Select(k => k.GetProperty("kind").GetString()));
        Assert.Equal("ready", kinds[^1].GetProperty("status").GetString());

        Assert.True(ForgeSession.TryLoadBySlug(_workspace, "demo", out var reloaded, out _));
        Assert.Equal(ForgeState.Ready, reloaded!.State);
        Assert.Equal(ForgeSessionStatus.Ready, reloaded.Status);
    }

    /// <summary>
    /// Anywhere else it refuses — and says so RECOVERABLY, finishing on the status the
    /// session still holds. Nothing moved on disk, so reporting the session as failed would
    /// be a verdict on the session rather than on the command that declined to move it.
    /// </summary>
    [Fact]
    public async Task Adopt_away_from_the_pause_refuses_without_moving_the_session()
    {
        var session = ForgeSession.Create(_workspace, "demo");
        session.SetState(ForgeState.Blueprint);
        session.Save();

        using var console = new TestConsole();
        var exitCode = await ForgeCommand.DispatchAsync(["resume", "demo", "--adopt", "--events", "jsonl"], _workspace);

        Assert.Equal(1, exitCode);
        var lines = console.Stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => System.Text.Json.JsonElement.Parse(line))
            .ToList();
        var error = Assert.Single(lines, l => l.GetProperty("kind").GetString() == "error");
        Assert.Equal("FORGE-INVALID-STATE", error.GetProperty("code").GetString());
        Assert.True(error.GetProperty("recoverable").GetBoolean());
        Assert.Equal("paused", lines[^1].GetProperty("status").GetString());

        Assert.True(ForgeSession.TryLoadBySlug(_workspace, "demo", out var reloaded, out _));
        Assert.Equal(ForgeState.Blueprint, reloaded!.State);
        Assert.Equal(ForgeSessionStatus.Active, reloaded.Status);
    }

    /// <summary>
    /// The pause is reachable AFTER a verdict — an amended blueprint and a refine both
    /// re-enter Test — and nothing on the way back clears the previous cycle's diagnosis.
    /// Adopting must not carry it out: the promoted card would credit a crew that no longer
    /// exists with a score, a pass/fail and findings it never earned.
    /// </summary>
    [Fact]
    public async Task Adopting_after_a_loop_back_leaves_no_verdict_of_an_earlier_crew_behind()
    {
        var session = ForgeSession.Create(_workspace, "demo");
        session.SetState(ForgeState.Test);
        session.SaveArtifact(ForgeSession.VerdictFileName, new { score = 0.9, passing = true });
        session.SaveArtifact(TestStage.LastRunFileName, new { tokens = 1200 });
        session.Save();
        Assert.True(session.HasVerdict);

        using var console = new TestConsole();
        Assert.Equal(0, await ForgeCommand.DispatchAsync(["resume", "demo", "--adopt", "--events", "jsonl"], _workspace));

        Assert.True(ForgeSession.TryLoadBySlug(_workspace, "demo", out var reloaded, out _));
        Assert.False(reloaded!.HasVerdict);
        Assert.False(File.Exists(Path.Combine(reloaded.Directory, TestStage.LastRunFileName)));
    }

    /// <summary>
    /// A budget that ran out at the pause must not lock the team in. That user is precisely
    /// the one who cannot pay for a trial, and adopting costs nothing at all.
    /// </summary>
    [Fact]
    public async Task A_budget_exhausted_pause_can_still_be_adopted()
    {
        var session = ForgeSession.Create(_workspace, "demo");
        session.SetState(ForgeState.Test);
        session.SetStatus(ForgeSessionStatus.BudgetExhausted);
        session.Save();

        using var console = new TestConsole();
        Assert.Equal(0, await ForgeCommand.DispatchAsync(["resume", "demo", "--adopt", "--events", "jsonl"], _workspace));

        Assert.True(ForgeSession.TryLoadBySlug(_workspace, "demo", out var reloaded, out _));
        Assert.Equal(ForgeSessionStatus.Ready, reloaded!.Status);
    }

    [Fact]
    public async Task Adopt_outside_a_resume_is_a_usage_error()
    {
        using var console = new TestConsole();

        Assert.Equal(1, await ForgeCommand.DispatchAsync(["une veille", "--adopt"], _workspace));
        Assert.Contains("--adopt only applies", console.Stderr, StringComparison.Ordinal);
    }

}
