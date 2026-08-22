using System.Text.Json;

namespace Orkeon.E2E.Tests;

/// <summary>
/// BUS-07: the real <c>orkeon run --events jsonl</c>, spawned as a process, fully offline —
/// the envelope it writes on failure, and a whole successful run through the echo-provider
/// fallback: the review found the original invariant only ever ran on a stream that could not
/// fault (two events, no payloads), so the null-omission and stdout-purity promises were
/// asserted precisely where they could not break.
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
    public void A_successful_run_keeps_stdout_pure_and_free_of_nulls()
    {
        // The rich half of the invariant: a real crew, run to success offline (no Llm section
        // → echo provider), emitting cost.updated and task.completed with actual payloads.
        // Two promises under test: every stdout line is an envelope — the human summary
        // banner used to land between two JSONL documents — and no key is ever null.
        var dir = Path.Combine(Path.GetTempPath(), $"orkeon-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "appsettings.json"), "{}");
            File.WriteAllText(Path.Combine(dir, "crew.yaml"), """
name: smoke-crew
goal: Run one task offline
process: "sequential"
agents:
  worker:
    role: Echoist
    goal: Echo things back
tasks:
  say:
    description: Say hello
    expected_output: A greeting
    agent: worker
""");

            var (exitCode, stdout, stderr) = _cli.RunCliSplit(
                $"run \"{Path.Combine(dir, "crew.yaml")}\" --events jsonl --allow-external-mounts " +
                $"--settings \"{Path.Combine(dir, "appsettings.json")}\"",
                _cli.RepoRoot, TimeSpan.FromMinutes(5));

            Assert.Equal(0, exitCode);

            // stdout carries the protocol and nothing else.
            var stdoutLines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Assert.All(stdoutLines, line => Assert.StartsWith("{\"v\":2", line, StringComparison.Ordinal));

            var events = ProtocolLines(stdout);
            Assert.Contains(events, e => e.GetProperty("kind").GetString() == "run.started");
            Assert.Contains(events, e => e.GetProperty("kind").GetString() == "task.completed");
            var finished = Assert.Single(events, e => e.GetProperty("kind").GetString() == "run.finished");
            Assert.True(finished.GetProperty("success").GetBoolean());

            foreach (var element in events)
            {
                foreach (var property in element.EnumerateObject())
                    Assert.NotEqual(JsonValueKind.Null, property.Value.ValueKind);
            }

            // The human summary moved, it did not vanish: the answer stays where humans read.
            Assert.Contains("Crew Output", stderr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
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
