using Orkeon.Host.Gateway;

namespace Orkeon.Host.Tests;

/// <summary>
/// GATE-03: progress is throttled, the acknowledgement and the final answer are not. A run
/// emits an event per agent thought and per tool call; relaying each one would exhaust a chat
/// platform's rate limit inside a single crew.
/// </summary>
public class ThrottledResponderTests
{
    private static InboundMessage Message() => new()
    {
        Channel = "test",
        ConversationId = "thread-1",
        SenderId = "trusted",
        Text = "do the thing",
        ReceivedAt = DateTimeOffset.UnixEpoch,
    };

    private static (ThrottledResponder Responder, RecordingResponder Inner, ManualClock Time) Build()
    {
        var inner = new RecordingResponder();
        var time = new ManualClock();
        return (new ThrottledResponder(inner, TimeSpan.FromSeconds(2), time), inner, time);
    }

    /// <summary>
    /// A clock that only moves when a test says so — the same shape the Domain's budget tests
    /// use, so a throttle window is exercised without anyone waiting two real seconds.
    /// </summary>
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }

    [Fact]
    public async Task A_burst_of_progress_becomes_one_update()
    {
        var (responder, inner, _) = Build();

        for (var index = 0; index < 20; index++)
            await responder.ProgressAsync(Message(), $"step {index}", TestContext.Current.CancellationToken);

        Assert.Single(inner.Sent);
        Assert.Equal("step 0", inner.Sent[0].Text);
    }

    [Fact]
    public async Task Progress_resumes_once_the_window_passes()
    {
        var (responder, inner, time) = Build();

        await responder.ProgressAsync(Message(), "first", TestContext.Current.CancellationToken);
        await responder.ProgressAsync(Message(), "suppressed", TestContext.Current.CancellationToken);

        time.Advance(TimeSpan.FromSeconds(3));
        await responder.ProgressAsync(Message(), "second", TestContext.Current.CancellationToken);

        Assert.Equal(["first", "second"], inner.Sent.Select(s => s.Text));
    }

    [Fact]
    public async Task The_last_suppressed_update_is_flushed_before_the_answer()
    {
        // Otherwise a run whose last progress line fell inside the window ends on silence,
        // and the user's last view of it is several steps stale.
        var (responder, inner, _) = Build();

        await responder.ProgressAsync(Message(), "first", TestContext.Current.CancellationToken);
        await responder.ProgressAsync(Message(), "reading the file", TestContext.Current.CancellationToken);
        await responder.ProgressAsync(Message(), "writing the answer", TestContext.Current.CancellationToken);

        await responder.CompleteAsync(Message(), "here it is", TestContext.Current.CancellationToken);

        Assert.Equal(
            ["first", "writing the answer", "here it is"],
            inner.Sent.Select(s => s.Text));

        // Only the newest suppressed line is flushed: a user catching up wants where the run
        // is now, not the three places it passed through.
        Assert.DoesNotContain(inner.Sent, s => s.Text == "reading the file");
    }

    [Fact]
    public async Task The_acknowledgement_and_the_answer_are_never_held_back()
    {
        var (responder, inner, _) = Build();

        await responder.AcknowledgeAsync(Message(), "on it", TestContext.Current.CancellationToken);
        await responder.CompleteAsync(Message(), "done", TestContext.Current.CancellationToken);

        Assert.Equal(["ack", "complete"], inner.Sent.Select(s => s.Kind));
    }
}
