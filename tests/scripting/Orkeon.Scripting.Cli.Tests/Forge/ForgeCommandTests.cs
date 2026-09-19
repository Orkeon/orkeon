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

    [Fact]
    public async Task Read_needs_a_directory()
    {
        using var console = new TestConsole();

        Assert.Equal(1, await ForgeCommand.DispatchAsync(["une veille", "--read"], _workspace));
        Assert.Contains("--read needs a directory", console.Stderr, StringComparison.Ordinal);

        // A following option is not a directory either.
        Assert.Equal(1, await ForgeCommand.DispatchAsync(["une veille", "--read", "--dry"], _workspace));
        Assert.Empty(ForgeSession.List(_workspace));
    }

    /// <summary>
    /// <c>promote</c> mounts nothing, so a read folder there would be ignored — and this
    /// parser never ignores an option silently.
    /// </summary>
    [Fact]
    public async Task Read_outside_a_new_session_or_a_resume_is_a_usage_error()
    {
        using var console = new TestConsole();

        var exitCode = await ForgeCommand.DispatchAsync(
            ["promote", "demo", "--to", Path.Combine(_workspace, "out"), "--read", _workspace], _workspace);

        Assert.Equal(1, exitCode);
        Assert.Contains("--read only applies", console.Stderr, StringComparison.Ordinal);

        // Where it applies, the parser takes it: the refusal that follows is about the
        // folder, not the option.
        var options = ForgeCommandOptions.Parse(["resume", "demo", "--read", "/data/notes"]);
        Assert.Null(options.Error);
        Assert.Equal("/data/notes", options.ReadDirectory);
        Assert.Equal("demo", options.ResumeSlug);
        Assert.Null(ForgeCommandOptions.Parse(["une", "veille", "--read", "/data/notes"]).Error);
    }

    /// <summary>
    /// A mistyped read folder is refused before anything moves: no session is created for
    /// it, and no host boots to discover an absent mount base the hard way.
    /// </summary>
    [Fact]
    public async Task A_read_directory_that_does_not_exist_is_refused_before_any_host_boots()
    {
        var missing = Path.Combine(_workspace, "nulle-part");
        using var console = new TestConsole();

        var exitCode = await ForgeCommand.DispatchAsync(["une", "veille", "--read", missing], _workspace);

        Assert.Equal(1, exitCode);
        Assert.Contains("--read names no directory", console.Stderr, StringComparison.Ordinal);
        Assert.Contains(missing, console.Stderr, StringComparison.Ordinal);
        // No host: the LLM refusal that a booted host would have produced is absent.
        Assert.DoesNotContain("FORGE-LLM-UNAVAILABLE", console.Stderr, StringComparison.Ordinal);
        Assert.Empty(ForgeSession.List(_workspace));

        // A resume refuses the same way, before the session is even looked up.
        Assert.Equal(1, await ForgeCommand.DispatchAsync(["resume", "ghost", "--read", missing], _workspace));
        Assert.DoesNotContain("ghost", console.Stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>--read</c> moves the documents, not the atelier: the read folder becomes the
    /// trial's <c>/workspace</c>, while the session keeps living under the workspace's own
    /// forge root — and the settings keep resolving next to the workspace.
    /// </summary>
    [Fact]
    public async Task Read_replaces_the_workspace_mount_and_leaves_the_session_root_under_the_workspace()
    {
        var documents = Path.Combine(Path.GetTempPath(), "orkeon-forge-read-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(documents);
        try
        {
            var session = ForgeSession.Create(_workspace, "une veille");

            var plan = ForgeCommand.BuildMountPlan(_workspace, documents, session);

            Assert.Equal(
                [
                    $"{Orkeon.Domain.FileSystem.FileSystemMount.Quote(documents)}:/workspace:ro",
                    $"{Orkeon.Domain.FileSystem.FileSystemMount.Quote(session.Directory)}:/forge:rw",
                    $"{Orkeon.Domain.FileSystem.FileSystemMount.Quote(Path.Combine(session.Directory, TestStage.OutputDirectoryName))}:/output:rw",
                ],
                plan.CliMounts);
            Assert.StartsWith(ForgeSession.RootFor(_workspace), session.Directory, StringComparison.Ordinal);
            // A folder outside the process cwd is whitelisted for the file tools, like the
            // script directory of `orkeon run`: the mount alone would register and then deny.
            Assert.True(plan.AllowExternalMounts);

            // Without --read, the workspace itself is read, as before, and nothing is whitelisted.
            var defaultPlan = ForgeCommand.BuildMountPlan(_workspace, null, session);
            Assert.Equal($"{Orkeon.Domain.FileSystem.FileSystemMount.Quote(_workspace)}:/workspace:ro", defaultPlan.CliMounts[0]);
            Assert.Equal(plan.CliMounts.Skip(1), defaultPlan.CliMounts.Skip(1));
            Assert.False(defaultPlan.AllowExternalMounts);

            // End to end, the cycle reaches the host with that plan: it refuses for want of an
            // LLM, and the session it created sits under the workspace, not under the documents.
            using var console = new TestConsole();
            Assert.Equal(1, await ForgeCommand.DispatchAsync(["resume", session.Document.Slug, "--read", documents], _workspace));
            Assert.Contains("FORGE-LLM-UNAVAILABLE", console.Stderr, StringComparison.Ordinal);
            Assert.Single(ForgeSession.List(_workspace));
            Assert.Empty(ForgeSession.List(documents));
        }
        finally
        {
            Directory.Delete(documents, recursive: true);
        }
    }

    /// <summary>
    /// A settings file naming <c>/output</c> or <c>/workspace</c> is the normal case, not a
    /// mistake: <c>/output</c> is an ordinary mount for a run and the name Studio gives a
    /// team's write folder, declared in the allowed folders the moment the wizard associates
    /// one. The forge's own mounts are placed by root against it (STUDIO-15 D-01), so each
    /// entry is replaced for the trial, said so in the log, and the cycle goes on to the host
    /// — which here refuses for want of an LLM, the proof that nothing refused it earlier.
    /// <para>
    /// The forge used to refuse such a file as a mistake, because before D-01 the entry and
    /// the forge's own mount reached the registry as two mounts and the run died with
    /// "Duplicate virtual paths: /output" out of a DI factory. That refusal outlived its
    /// reason: the team composer writes <c>/output</c> into the settings the moment a team
    /// produces a deliverable, and the wizard declares the folder associated at step 1 under
    /// that very name — so it stopped the first trial of every Studio team on a real folder.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_settings_file_naming_the_forge_roots_is_placed_by_root_not_refused()
    {
        var documents = Directory.CreateDirectory(Path.Combine(_workspace, "factures")).FullName;
        var output = Directory.CreateDirectory(Path.Combine(_workspace, "sortie")).FullName;
        var settings = WriteSettings(
            $"{Orkeon.Domain.FileSystem.FileSystemMount.Quote(documents)}:/workspace:ro",
            $"{Orkeon.Domain.FileSystem.FileSystemMount.Quote(output)}:/output:rw");
        using var console = new TestConsole();

        var exitCode = await ForgeCommand.DispatchAsync(["une", "veille", "--settings", settings, "--dry"], _workspace);

        Assert.Equal(1, exitCode);
        Assert.DoesNotContain("reserved by the runner", console.Stderr, StringComparison.Ordinal);
        Assert.Contains("FORGE-LLM-UNAVAILABLE", console.Stderr, StringComparison.Ordinal);
        Assert.Contains("mount /workspace: --mount replaces the settings entry", console.Stderr, StringComparison.Ordinal);
        Assert.Contains("mount /output: --mount replaces the settings entry", console.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("Duplicate virtual paths", console.Stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>/sandbox</c> stays reserved: the forge does not mount it — the file system registers
    /// it internally in every host — so nothing places a settings entry claiming it, and the
    /// refusal is the one line the guard exists for, before any host boots.
    /// </summary>
    [Fact]
    public async Task A_settings_file_claiming_the_sandbox_root_is_still_refused_before_any_host()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_workspace, "bac")).FullName;
        var settings = WriteSettings($"{Orkeon.Domain.FileSystem.FileSystemMount.Quote(folder)}:/sandbox:rw");
        using var console = new TestConsole();

        var exitCode = await ForgeCommand.DispatchAsync(["une", "veille", "--settings", settings, "--dry"], _workspace);

        Assert.Equal(1, exitCode);
        Assert.Contains("reserved by the runner", console.Stderr, StringComparison.Ordinal);
        Assert.Contains("/sandbox", console.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("FORGE-LLM-UNAVAILABLE", console.Stderr, StringComparison.Ordinal);
    }

    /// <summary>A settings file declaring <paramref name="mounts"/> and no LLM, under the workspace.</summary>
    private string WriteSettings(params string[] mounts)
    {
        Directory.CreateDirectory(_workspace);
        var path = Path.Combine(_workspace, "trial-settings.json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new
        {
            Orkeon = new { FileSystem = new { Mounts = mounts } },
        }));
        return path;
    }
}
