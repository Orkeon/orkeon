using System.Text.Json;

namespace Orkeon.E2E.Tests;

/// <summary>
/// BUS-07: the real <c>orkeon run --events jsonl</c>, spawned as a process, on its fully offline
/// half — the envelope it writes, and the promise that a failure before the host even builds is
/// reported as an event rather than as silence. A successful run needs a model; the protocol's
/// shape does not.
/// </summary>
[Trait("Category", "Slow")]
[Collection(OrkeonCliFixture.CollectionName)]
public sealed class RunEventsCliOfflineSlowTests
{
    private readonly OrkeonCliFixture _cli;

    /// <summary>Takes the shared CLI build; see <see cref="OrkeonCliFixture"/> for why.</summary>
    public RunEventsCliOfflineSlowTests(OrkeonCliFixture cli) => _cli = cli;

    private static IReadOnlyList<JsonElement> ProtocolLines(string output) =>
        [.. output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith('{'))
            .Select(TryParse)
            .Where(element => element is not null)
            .Select(element => element!.Value)
            .Where(element => element.TryGetProperty("kind", out _))];

    private static JsonElement? TryParse(string line)
    {
        try
        {
            return JsonDocument.Parse(line).RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [Fact]
    public void A_run_that_cannot_start_still_opens_and_closes_the_stream()
    {
        // The stream opens before the host is built and closes on the runner's own exit code.
        // A configuration failure reported as silence would leave a watching screen hanging on
        // a run that already died.
        var (exitCode, output) = _cli.RunCli(
            "run ./this-crew-does-not-exist.yaml --events jsonl", _cli.RepoRoot, TimeSpan.FromMinutes(5));

        Assert.NotEqual(0, exitCode);

        var events = ProtocolLines(output);
        Assert.Contains(events, e => e.GetProperty("kind").GetString() == "run.started");

        var finished = Assert.Single(events, e => e.GetProperty("kind").GetString() == "run.finished");
        Assert.False(finished.GetProperty("success").GetBoolean());
        Assert.Equal(exitCode, finished.GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public void Every_line_of_the_stream_carries_the_envelope()
    {
        var (_, output) = _cli.RunCli(
            "run ./this-crew-does-not-exist.yaml --events jsonl", _cli.RepoRoot, TimeSpan.FromMinutes(5));

        var events = ProtocolLines(output);
        Assert.NotEmpty(events);

        long previousSeq = 0;
        foreach (var element in events)
        {
            Assert.Equal(2, element.GetProperty("v").GetInt32());
            Assert.NotEmpty(element.GetProperty("ts").GetString()!);
            Assert.NotEmpty(element.GetProperty("kind").GetString()!);

            // seq is monotonic within a run: a client uses it to detect a line it never saw.
            var seq = element.GetProperty("seq").GetInt64();
            Assert.True(seq > previousSeq, $"seq went backwards: {previousSeq} then {seq}");
            previousSeq = seq;

            // An absent key is omitted, never written as null — every identity field is
            // optional, and a client must not have to tell "absent" from "explicitly nothing".
            foreach (var property in element.EnumerateObject())
                Assert.NotEqual(JsonValueKind.Null, property.Value.ValueKind);
        }
    }

    [Fact]
    public void Without_the_flag_the_run_still_prints_for_a_human()
    {
        // The protocol is opt-in. A run nobody asked to watch must keep its plain rendering,
        // which is also the tag criterion: --events changes nothing for anyone not passing it.
        var (_, output) = _cli.RunCli(
            "run ./this-crew-does-not-exist.yaml", _cli.RepoRoot, TimeSpan.FromMinutes(5));

        Assert.Empty(ProtocolLines(output));
    }
}
