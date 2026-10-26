using System.Globalization;
using Orkeon.Studio.Core.History;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Wpf.ViewModels.Launch;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// The History screen of the refonte (lot 5): the runs grouped by day, the team and status
/// filters, the team's own name rather than its folder, and « &lt; 1 s » for a run under a second.
/// </summary>
public sealed class LaunchHistoryScreenTests
{
    // A day far from any daylight-saving change, so "yesterday" is always 24 hours back.
    private static readonly DateTimeOffset Now = LocalNoon(new DateTime(2026, 6, 10));

    private static DateTimeOffset LocalNoon(DateTime day)
    {
        var noon = day.Date.AddHours(12);
        return new DateTimeOffset(noon, TimeZoneInfo.Local.GetUtcOffset(noon));
    }

    private static LaunchHistoryEntry Run(string target, DateTimeOffset at, RunOutcome outcome, double? seconds = 3) => new()
    {
        Target = target,
        StartedAt = at,
        Outcome = outcome,
        ExitCode = outcome == RunOutcome.Success ? 0 : 2,
        DurationSeconds = seconds,
    };

    private static LaunchHistoryViewModel Build(Func<string, string?>? teamNameOf = null, params LaunchHistoryEntry[] entries)
    {
        var history = new LaunchHistoryViewModel(teamNameOf: teamNameOf, now: () => Now);
        history.Publish(new LaunchHistory { Entries = entries });
        return history;
    }

    [Fact]
    public void Runs_are_grouped_by_day_today_yesterday_then_the_date_most_recent_first()
    {
        var older = LocalNoon(new DateTime(2026, 6, 6));
        var history = Build(null,
            Run("/teams/alpha", older, RunOutcome.Success),
            Run("/teams/alpha", Now.AddHours(-1), RunOutcome.Success),
            Run("/teams/beta", Now.AddDays(-1), RunOutcome.RuntimeError),
            Run("/teams/beta", Now.AddHours(-3), RunOutcome.Cancelled));

        var strings = EnglishStudioStrings.Instance;
        Assert.Equal(
            [strings[StudioStringKeys.HistToday], strings[StudioStringKeys.HistYesterday],
             LaunchHistoryViewModel.SentenceCase(older.Date.ToString("D", CultureInfo.CurrentCulture))],
            history.Groups.Select(group => group.Header));
        Assert.Equal([Now.AddHours(-1), Now.AddHours(-3)], history.Groups[0].Entries.Select(row => row.StartedAt));
        Assert.False(history.HasNoMatch);
    }

    [Fact]
    public void A_dated_header_starts_with_a_capital_in_the_culture_that_writes_it()
    {
        // D05: fr writes « vendredi 28 août 2026 »; the header reads « Vendredi 28 août 2026 ».
        var kept = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal("Vendredi 28 août 2026", LaunchHistoryViewModel.SentenceCase(
                new DateTime(2026, 8, 28).ToString("D", CultureInfo.CurrentCulture)));
        }
        finally
        {
            CultureInfo.CurrentCulture = kept;
        }

        Assert.Equal("", LaunchHistoryViewModel.SentenceCase(""));
        Assert.Equal("Aujourd'hui", LaunchHistoryViewModel.SentenceCase("Aujourd'hui"));
    }

    [Fact]
    public void The_usage_parts_break_only_between_two_values()
    {
        // B11: « 6 020 tokens · cache 10 % · 602 tokens » never wraps inside a value; each part after
        // the first carries its « · », and the parts say exactly what the joined line says.
        var entry = Run("/teams/alpha", Now.AddHours(-1), RunOutcome.Success) with
        {
            Tokens = 6020, CacheHitTokens = 602, CacheMissTokens = 5418,
        };
        var row = Build(null, entry).Groups[0].Entries[0];

        Assert.True(row.UsageParts.Count > 1);
        Assert.DoesNotContain("·", row.UsageParts[0], StringComparison.Ordinal);
        Assert.All(row.UsageParts.Skip(1), part => Assert.StartsWith("· ", part, StringComparison.Ordinal));
        Assert.Equal(row.UsageText, string.Join(" ", row.UsageParts));
    }

    [Fact]
    public void The_team_filter_lists_each_team_once_and_keeps_only_its_runs()
    {
        var history = Build(null,
            Run("/teams/alpha", Now.AddHours(-1), RunOutcome.Success),
            Run("/teams/beta", Now.AddHours(-2), RunOutcome.Success),
            Run("/teams/alpha/", Now.AddHours(-3), RunOutcome.RuntimeError));

        Assert.Equal(3, history.TeamFilters.Count);
        Assert.Null(history.TeamFilter!.Team);

        history.TeamFilter = history.TeamFilters.Single(option => option.Label == "alpha");

        Assert.Equal(2, history.Groups.Single().Entries.Count);
        Assert.All(history.Groups.Single().Entries, row => Assert.Equal("alpha", row.TeamName));
    }

    [Fact]
    public void The_status_filter_keeps_only_the_runs_of_that_status_and_says_when_none_is_left()
    {
        var history = Build(null,
            Run("/teams/alpha", Now.AddHours(-1), RunOutcome.Success),
            Run("/teams/alpha", Now.AddHours(-2), RunOutcome.ScriptError));

        history.StatusFilter = history.StatusFilters.Single(option => option.Tone == "fail");
        Assert.Equal(RunOutcome.ScriptError, history.Groups.Single().Entries.Single().Outcome);

        history.StatusFilter = history.StatusFilters.Single(option => option.Tone == "warn");
        Assert.Empty(history.Groups);
        Assert.True(history.HasNoMatch);
        Assert.False(history.IsEmpty);
    }

    [Fact]
    public void A_picked_filter_survives_a_new_run()
    {
        var first = Run("/teams/alpha", Now.AddHours(-2), RunOutcome.Success);
        var history = Build(null, first, Run("/teams/beta", Now.AddHours(-3), RunOutcome.Success));
        history.TeamFilter = history.TeamFilters.Single(option => option.Label == "beta");

        history.Publish(new LaunchHistory { Entries = [Run("/teams/alpha", Now.AddHours(-1), RunOutcome.Success), first, Run("/teams/beta", Now.AddHours(-3), RunOutcome.Success)] });

        Assert.Equal("beta", history.TeamFilter!.Label);
        Assert.Equal("beta", history.Groups.Single().Entries.Single().TeamName);
    }

    [Fact]
    public void A_line_names_the_team_by_its_own_name_and_reads_its_status_and_duration()
    {
        var history = Build(target => target == "/teams/sales-desk" ? "Sales desk" : null,
            Run("/teams/sales-desk", Now.AddHours(-1), RunOutcome.Cancelled, seconds: 0.4),
            Run("/teams/other", Now.AddHours(-2), RunOutcome.Success, seconds: 125));

        var rows = history.Groups.Single().Entries;
        Assert.Equal("Sales desk", rows[0].TeamName);
        Assert.Equal("< 1 s", rows[0].DurationText);
        Assert.Equal(EnglishStudioStrings.Instance[StudioStringKeys.HistStatusCancelled], rows[0].StatusText);
        Assert.Equal("Warning", rows[0].BadgeTone);
        Assert.Equal("other", rows[1].TeamName);
        Assert.Equal("Success", rows[1].BadgeTone);
        Assert.Contains("2 min", rows[1].DurationText, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_history_shows_the_empty_phrase_not_the_no_match_one()
    {
        var history = Build();

        Assert.True(history.IsEmpty);
        Assert.False(history.HasNoMatch);
        Assert.Empty(history.Groups);
    }
}
