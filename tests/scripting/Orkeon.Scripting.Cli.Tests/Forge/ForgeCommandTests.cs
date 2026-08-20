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
}
