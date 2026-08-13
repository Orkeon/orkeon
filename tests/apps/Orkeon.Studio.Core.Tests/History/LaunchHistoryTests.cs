using Orkeon.Studio.Core.History;
using Orkeon.Studio.Core.Process;

namespace Orkeon.Studio.Core.Tests.History;

/// <summary>The in-memory history model: ordering, the bound, and the JSON round-trip.</summary>
public sealed class LaunchHistoryTests
{
    private static LaunchHistoryEntry Entry(string target, int minutesAgo = 0) =>
        LaunchHistoryEntry.Starting(
            target,
            ["run", target],
            settingsPath: "/home/me/.config/Orkeon/appsettings.json",
            workingDirectory: "/home/me",
            startedAt: DateTimeOffset.UnixEpoch.AddMinutes(-minutesAgo));

    [Fact]
    public void The_newest_launch_comes_first()
    {
        var history = LaunchHistory.Empty
            .Add(Entry("first.yaml"))
            .Add(Entry("second.yaml"));

        Assert.Equal(["second.yaml", "first.yaml"], history.Entries.Select(e => e.Target));
    }

    [Fact]
    public void The_history_never_grows_past_its_bound()
    {
        var history = LaunchHistory.Empty;
        for (var i = 0; i < LaunchHistory.MaxEntries + 20; i++)
            history = history.Add(Entry($"crew-{i}.yaml"));

        Assert.Equal(LaunchHistory.MaxEntries, history.Entries.Count);

        // The bound drops the oldest, not the newest.
        Assert.Equal($"crew-{LaunchHistory.MaxEntries + 19}.yaml", history.Entries[0].Target);
        Assert.Equal($"crew-20.yaml", history.Entries[^1].Target);
    }

    [Fact]
    public void An_entry_carries_everything_needed_to_replay_the_launch()
    {
        var entry = Entry("crew.yaml").WithResult(ProcessRunResult.FromExitCode(1, TimeSpan.FromSeconds(2)));

        Assert.Equal("crew.yaml", entry.Target);
        Assert.Equal(["run", "crew.yaml"], entry.Arguments);
        Assert.Equal("/home/me/.config/Orkeon/appsettings.json", entry.SettingsPath);
        Assert.Equal("/home/me", entry.WorkingDirectory);
        Assert.Equal(1, entry.ExitCode);
        Assert.Equal(RunOutcome.ScriptError, entry.Outcome);
    }

    [Fact]
    public void A_launch_that_never_started_records_no_exit_code()
    {
        var entry = Entry("crew.yaml").WithResult(ProcessRunResult.NotStarted("orkeon not found"));

        Assert.Null(entry.ExitCode);
        Assert.Equal(RunOutcome.NotStarted, entry.Outcome);
    }

    [Fact]
    public void A_history_survives_a_json_round_trip()
    {
        var original = LaunchHistory.Empty
            .Add(Entry("first.yaml", minutesAgo: 10).WithResult(ProcessRunResult.FromExitCode(0, TimeSpan.FromSeconds(1))))
            .Add(Entry("second crew.yaml").WithResult(
                ProcessRunResult.FromCancellation(
                    137, ProcessTerminationOutcome.Of(ProcessTerminationMode.Killed), TimeSpan.FromSeconds(4))));

        Assert.True(LaunchHistory.TryParse(original.ToJson(), out var reloaded, out var error));

        Assert.Null(error);
        Assert.Equal(original.Entries.Count, reloaded.Entries.Count);
        AssertSameEntry(original.Entries[0], reloaded.Entries[0]);
        AssertSameEntry(original.Entries[1], reloaded.Entries[1]);
        Assert.Equal(RunOutcome.Cancelled, reloaded.Entries[0].Outcome);
        Assert.Equal(OrkeonExitCodes.Cancelled, reloaded.Entries[0].ExitCode);
    }

    /// <summary>
    /// Field-by-field, because the record's generated equality compares the argument list by
    /// reference — a reloaded entry is never the same object as the one that was written.
    /// </summary>
    internal static void AssertSameEntry(LaunchHistoryEntry expected, LaunchHistoryEntry actual)
    {
        Assert.Equal(expected.Target, actual.Target);
        Assert.Equal(expected.SettingsPath, actual.SettingsPath);
        Assert.Equal(expected.WorkingDirectory, actual.WorkingDirectory);
        Assert.Equal(expected.StartedAt, actual.StartedAt);
        Assert.Equal(expected.ExitCode, actual.ExitCode);
        Assert.Equal(expected.Outcome, actual.Outcome);
        Assert.Equal(expected.Arguments, actual.Arguments);
    }

    [Fact]
    public void Outcomes_are_stored_by_name_so_the_file_stays_readable()
    {
        var json = LaunchHistory.Empty
            .Add(Entry("crew.yaml").WithResult(ProcessRunResult.FromExitCode(2, TimeSpan.Zero)))
            .ToJson();

        Assert.Contains("\"RuntimeError\"", json, StringComparison.Ordinal);
        Assert.Contains("\"target\"", json, StringComparison.Ordinal);
        Assert.Contains("\"started_at\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_written_past_the_bound_is_truncated_on_read()
    {
        // A hand-edited or older-version file must not defeat the bound.
        var entries = string.Join(",", Enumerable.Range(0, 120).Select(i =>
            $$"""{"target":"crew-{{i}}.yaml","started_at":"1970-01-01T00:00:00+00:00","outcome":"Success"}"""));

        Assert.True(LaunchHistory.TryParse($$"""{"entries":[{{entries}}]}""", out var history, out _));

        Assert.Equal(LaunchHistory.MaxEntries, history.Entries.Count);
    }

    [Fact]
    public void Entries_without_a_target_are_dropped()
    {
        const string json =
            """{"entries":[{"target":"  ","started_at":"1970-01-01T00:00:00+00:00"},{"target":"crew.yaml","started_at":"1970-01-01T00:00:00+00:00"}]}""";

        Assert.True(LaunchHistory.TryParse(json, out var history, out _));

        Assert.Equal("crew.yaml", Assert.Single(history.Entries).Target);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("""{"entries":"nope"}""")]
    public void An_unreadable_history_yields_an_empty_one_with_a_reason(string? json)
    {
        Assert.False(LaunchHistory.TryParse(json, out var history, out var error));

        Assert.Empty(history.Entries);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
