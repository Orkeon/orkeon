using System.Text.Json;
using Orkeon.Scripting.Cli.Commands.Forge;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>
/// The session on disk (SPEC-ORKEON-FORGE §4.1): create, round-trip, resume, collisions,
/// the append-only transition history, and the listing that skips corruption instead of
/// dying on it.
/// </summary>
public sealed class ForgeSessionTests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "orkeon-forge-session-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    [Fact]
    public void A_fresh_session_writes_its_file_under_the_forge_root()
    {
        var session = ForgeSession.Create(_workspace, "Veille fournisseurs", now: FixedNow);

        Assert.Equal("veille-fournisseurs", session.Document.Slug);
        Assert.True(File.Exists(Path.Combine(session.Directory, ForgeSession.SessionFileName)));
        Assert.StartsWith(ForgeSession.RootFor(_workspace), session.Directory, StringComparison.Ordinal);
        Assert.Equal(ForgeState.Brief, session.State);
        Assert.Equal(ForgeSessionStatus.Active, session.Status);
        Assert.Equal("2026-08-19T12:00:00Z", session.Document.CreatedAt);
    }

    [Fact]
    public void A_session_round_trips_its_state_budget_and_format()
    {
        var created = ForgeSession.Create(
            _workspace, "demo", format: "script",
            budget: new ForgeBudget { MaxIterations = 5, MaxTokens = 10_000 }, now: FixedNow);
        created.SetState(ForgeState.Validate);
        created.SetStatus(ForgeSessionStatus.Active);
        created.Document.Budget.RegisterIteration();
        created.Document.Budget.RegisterTokens(new ForgeUsageSnapshot(1000, 234, 0));
        created.Save(FixedNow);

        Assert.True(ForgeSession.TryLoad(created.Directory, out var loaded, out var error), error);
        Assert.Equal(ForgeState.Validate, loaded!.State);
        Assert.Equal("script", loaded.Document.Format);
        Assert.Equal(5, loaded.Document.Budget.MaxIterations);
        Assert.Equal(10_000, loaded.Document.Budget.MaxTokens);
        Assert.Equal(1, loaded.Document.Budget.ConsumedIterations);
        Assert.Equal(1234, loaded.Document.Budget.ConsumedTokens);
    }

    [Fact]
    public void A_slug_collision_gets_a_numeric_suffix()
    {
        var first = ForgeSession.Create(_workspace, "demo", now: FixedNow);
        var second = ForgeSession.Create(_workspace, "demo", now: FixedNow);
        var third = ForgeSession.Create(_workspace, "demo", now: FixedNow);

        Assert.Equal("demo", first.Document.Slug);
        Assert.Equal("demo-2", second.Document.Slug);
        Assert.Equal("demo-3", third.Document.Slug);
    }

    [Theory]
    [InlineData("Résumer les offres, chaque matin !", "resumer-les-offres-chaque-matin")]
    [InlineData("  UPPER case  ", "upper-case")]
    [InlineData("///", null)]
    public void Requested_slugs_are_kebab_cased_or_fall_back_to_a_stamp(string requested, string? expected)
    {
        var session = ForgeSession.Create(_workspace, requested, now: FixedNow);

        if (expected is null)
            Assert.Equal("forge-20260819-120000", session.Document.Slug);
        else
            Assert.Equal(expected, session.Document.Slug);
    }

    [Fact]
    public void A_corrupt_session_file_reports_an_error_instead_of_throwing()
    {
        var session = ForgeSession.Create(_workspace, "demo", now: FixedNow);
        File.WriteAllText(Path.Combine(session.Directory, ForgeSession.SessionFileName), "{ not json");

        Assert.False(ForgeSession.TryLoad(session.Directory, out var loaded, out var error));
        Assert.Null(loaded);
        Assert.Contains(ForgeSession.SessionFileName, error, StringComparison.Ordinal);
    }

    [Fact]
    public void The_listing_skips_corruption_and_orders_by_recency()
    {
        var older = ForgeSession.Create(_workspace, "older", now: FixedNow);
        older.Save(FixedNow.AddMinutes(-30));
        var newer = ForgeSession.Create(_workspace, "newer", now: FixedNow);
        newer.Save(FixedNow.AddMinutes(5));
        var corrupt = ForgeSession.Create(_workspace, "corrupt", now: FixedNow);
        File.WriteAllText(Path.Combine(corrupt.Directory, ForgeSession.SessionFileName), "boom");

        var listed = ForgeSession.List(_workspace);

        Assert.Equal(["newer", "older"], listed.Select(s => s.Slug));
    }

    [Fact]
    public void Loading_by_slug_names_the_root_when_nothing_matches()
    {
        Assert.False(ForgeSession.TryLoadBySlug(_workspace, "ghost", out _, out var error));
        Assert.Contains("ghost", error, StringComparison.Ordinal);
        Assert.Contains(ForgeSession.RootFor(_workspace), error, StringComparison.Ordinal);
    }

    [Fact]
    public void The_transition_history_is_append_only_jsonl()
    {
        var session = ForgeSession.Create(_workspace, "demo", now: FixedNow);
        session.AppendHistory(ForgeState.Brief, ForgeTrigger.BriefSubmitted, ForgeState.Blueprint, FixedNow);
        session.AppendHistory(ForgeState.Blueprint, ForgeTrigger.BlueprintSubmitted, ForgeState.Render, FixedNow);

        var lines = File.ReadAllLines(Path.Combine(session.Directory, ForgeSession.HistoryFileName));

        Assert.Equal(2, lines.Length);
        var first = JsonDocument.Parse(lines[0]).RootElement;
        Assert.Equal("Brief", first.GetProperty("from").GetString());
        Assert.Equal("BriefSubmitted", first.GetProperty("trigger").GetString());
        Assert.Equal("Blueprint", first.GetProperty("to").GetString());
        Assert.Equal("2026-08-19T12:00:00Z", first.GetProperty("ts").GetString());
    }

    [Fact]
    public void An_empty_workspace_lists_nothing_without_creating_the_root()
    {
        Assert.Empty(ForgeSession.List(_workspace));
        Assert.False(Directory.Exists(ForgeSession.RootFor(_workspace)));
    }

    [Fact]
    public void Anything_that_reached_a_verdict_can_be_reopened_at_the_arbitration()
    {
        // W-09: a promoted session reopens; an abandoned one only if it has a verdict —
        // one abort after a reopen must never strand the team forever.
        var promoted = ForgeSession.Create(_workspace, "promue");
        promoted.SetState(ForgeState.Promoted);
        promoted.SetStatus(ForgeSessionStatus.Promoted);
        promoted.Save(FixedNow);

        Assert.True(promoted.TryReopen(FixedNow));
        Assert.Equal(ForgeState.Verdict, promoted.State);
        Assert.Equal(ForgeSessionStatus.Active, promoted.Status);
        Assert.Contains("\"trigger\":\"Reopen\"",
            File.ReadAllText(Path.Combine(promoted.Directory, ForgeSession.HistoryFileName)),
            StringComparison.OrdinalIgnoreCase);

        var abandonedWithVerdict = ForgeSession.Create(_workspace, "avec-verdict");
        abandonedWithVerdict.SaveArtifact("verdict.json", new ForgeVerdict { Score = 0.5, Judge = ForgeVerdict.JudgeDeterministic });
        abandonedWithVerdict.SetState(ForgeState.Abandoned);
        abandonedWithVerdict.SetStatus(ForgeSessionStatus.Abandoned);
        abandonedWithVerdict.Save(FixedNow);
        Assert.True(abandonedWithVerdict.TryReopen(FixedNow));

        var abandonedBlind = ForgeSession.Create(_workspace, "sans-verdict");
        abandonedBlind.SetState(ForgeState.Abandoned);
        abandonedBlind.SetStatus(ForgeSessionStatus.Abandoned);
        abandonedBlind.Save(FixedNow);
        Assert.False(abandonedBlind.TryReopen(FixedNow));

        var active = ForgeSession.Create(_workspace, "active");
        Assert.False(active.TryReopen(FixedNow));
    }
}
