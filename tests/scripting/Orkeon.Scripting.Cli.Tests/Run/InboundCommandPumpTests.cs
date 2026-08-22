using System.Collections.Concurrent;
using Orkeon.Scripting.Cli.Commands.Run;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>
/// A stdin stand-in fed line by line, so the pump can be driven mid-flight. Completing it is
/// the test's way of closing the channel (EOF).
/// </summary>
internal sealed class FeedableReader : TextReader
{
    private readonly BlockingCollection<string> _lines = [];

    public void Feed(string line) => _lines.Add(line);

    public void Close_() => _lines.CompleteAdding();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _lines.Dispose();
        base.Dispose(disposing);
    }

    public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        return new ValueTask<string?>(Task.Run(() =>
        {
            try
            {
                return _lines.Take(cancellationToken);
            }
            catch (InvalidOperationException)
            {
                return (string?)null;   // completed: EOF
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }, CancellationToken.None));
    }
}

/// <summary>
/// The single stdin reader (BUS-05). The behaviours pinned here are the ones the first
/// version got wrong: a pump that only woke up for human questions, a read loop that executed
/// commands in-line, and a waiter queue that leaked, lost and reordered.
/// </summary>
public sealed class InboundCommandPumpTests
{
    private static string Answer(string value, string? correlationId = null) =>
        correlationId is null
            ? $$"""{"kind":"input.given","value":"{{value}}"}"""
            : $$"""{"kind":"input.given","correlationId":"{{correlationId}}","value":"{{value}}"}""";

    [Fact]
    public async Task A_command_flows_without_any_human_question_ever_asked()
    {
        // The defect this pins shut: EnsureRunning used to be called only by ReadAnswerAsync,
        // so a run whose crew asked no question never read stdin at all — every post/send/
        // subscribe line of BUS-05 arrived into the void.
        var reader = new FeedableReader();
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var pump = new InboundCommandPump(reader, (line, _) =>
        {
            received.TrySetResult(line);
            return Task.CompletedTask;
        });

        pump.EnsureRunning();
        reader.Feed("""{"kind":"post","to":"agent://x/y","payload":{}}""");

        var line = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Contains("\"post\"", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_slow_command_does_not_block_the_answers_behind_it()
    {
        // The read loop parses and queues; a worker executes. Without that split, a `send`
        // holding its 30 s timeout would hold stdin too — and the very reply the peer is
        // trying to deliver could never be read.
        var reader = new FeedableReader();
        var commandStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var pump = new InboundCommandPump(reader, async (_, _) =>
        {
            commandStarted.TrySetResult();
            await release.Task.ConfigureAwait(false);
        });

        var waiter = pump.ReadAnswerAsync("q1", TestContext.Current.CancellationToken);
        reader.Feed("""{"kind":"send","to":"agent://x/y","payload":{}}""");
        await commandStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        reader.Feed(Answer("oui"));
        Assert.Equal("oui", await waiter.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        release.TrySetResult();
    }

    [Fact]
    public async Task An_unnamed_answer_skips_a_cancelled_waiter_instead_of_dying_on_it()
    {
        // TrySetResult on a dead waiter returns false — an answer spent on the corpse would
        // be lost for the live waiter behind it, which is precisely the human's answer.
        var reader = new FeedableReader();
        await using var pump = new InboundCommandPump(reader);

        using var cancelled = new CancellationTokenSource();
        var dead = pump.ReadAnswerAsync("q1", cancelled.Token);
        var alive = pump.ReadAnswerAsync("q2", TestContext.Current.CancellationToken);

        await cancelled.CancelAsync();
        Assert.Null(await dead.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        reader.Feed(Answer("bleu"));
        Assert.Equal("bleu", await alive.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_named_answer_does_not_reorder_the_waiters_it_skips()
    {
        // The old queue re-enqueued skipped waiters at the tail, so after one named answer
        // "oldest" no longer meant oldest — and the next unnamed answer went to the wrong
        // question.
        var reader = new FeedableReader();
        await using var pump = new InboundCommandPump(reader);

        var first = pump.ReadAnswerAsync("q1", TestContext.Current.CancellationToken);
        var second = pump.ReadAnswerAsync("q2", TestContext.Current.CancellationToken);
        var third = pump.ReadAnswerAsync("q3", TestContext.Current.CancellationToken);

        reader.Feed(Answer("pour-le-deuxieme", correlationId: "q2"));
        Assert.Equal("pour-le-deuxieme", await second.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        // The next unnamed answer must reach q1 — the true oldest — not q3.
        reader.Feed(Answer("pour-le-premier"));
        Assert.Equal("pour-le-premier", await first.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        reader.Feed(Answer("pour-le-troisieme"));
        Assert.Equal("pour-le-troisieme", await third.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_channel_closing_refuses_everyone_including_latecomers()
    {
        var reader = new FeedableReader();
        await using var pump = new InboundCommandPump(reader);

        var pending = pump.ReadAnswerAsync("q1", TestContext.Current.CancellationToken);
        reader.Close_();

        // Silence is an answer, and it means refusal.
        Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        // A waiter arriving after EOF must not hang until its own token fires: nobody will
        // ever read again, and the pump says so immediately.
        Assert.Null(await pump.ReadAnswerAsync("q2", TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Dispose_returns_even_while_a_read_is_parked_on_a_silent_stream()
    {
        // Console stdin has no truly cancellable read. A dispose that awaited the loop held
        // the whole process hostage after run.finished whenever the parent (Studio) kept our
        // stdin open — the pump abandons the read instead.
        var reader = new FeedableReader();
        var pump = new InboundCommandPump(reader);
        pump.EnsureRunning();

        var pending = pump.ReadAnswerAsync("q1", TestContext.Current.CancellationToken);

        await pump.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }
}
