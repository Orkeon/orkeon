using Orkeon.Host.Gateway;

namespace Orkeon.Host.Tests;

using Orkeon.Host.Tests.Doubles;

/// <summary>
/// GATE-03: progress is throttled, the acknowledgement and the final answer are not. A run
/// emits an event per agent thought and per tool call; relaying each one would exhaust a chat
/// platform's rate limit inside a single crew.
/// </summary>
public class ThrottledResponderTests
{
    private static InboundMessage Message(string conversation = "thread-1") => new()
    {
        Channel = "test",
        ConversationId = conversation,
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
    public async Task The_idle_sweep_never_drops_a_window_still_holding_a_suppressed_line()
    {
        var (responder, inner, time) = Build();
        var ct = TestContext.Current.CancellationToken;

        await responder.ProgressAsync(Message("busy"), "first", ct);
        await responder.ProgressAsync(Message("busy"), "still pending", ct);
        for (var index = 0; index < ThrottledResponder.SweepThreshold + 2; index++)
            await responder.ProgressAsync(Message($"idle-{index}"), $"idle {index}", ct);

        // Every window is now idle for longer than the sweep tolerates; a completion sweeps.
        time.Advance(TimeSpan.FromSeconds(2) * (ThrottledResponder.IdleIntervals + 1));
        await responder.CompleteAsync(Message("idle-0"), "done 0", ct);
        inner.Sent.Clear();

        await responder.CompleteAsync(Message("busy"), "busy done", ct);
        await responder.ProgressAsync(Message("idle-1"), "idle again", ct);

        Assert.Equal(["still pending", "busy done", "idle again"], inner.Sent.Select(s => s.Text));
    }

    [Fact]
    public void A_responder_needs_an_inner_responder()
    {
        Assert.Throws<ArgumentNullException>(() => new ThrottledResponder(null!));
        Assert.Equal(TimeSpan.FromSeconds(2), ThrottledResponder.DefaultInterval);
    }

    [Fact]
    public async Task The_acknowledgement_and_the_answer_are_never_held_back()
    {
        var (responder, inner, _) = Build();

        await responder.AcknowledgeAsync(Message(), "on it", TestContext.Current.CancellationToken);
        await responder.CompleteAsync(Message(), "done", TestContext.Current.CancellationToken);

        Assert.Equal(["ack", "complete"], inner.Sent.Select(s => s.Kind));
    }

    [Fact]
    public async Task Two_conversations_have_two_windows_and_never_swap_content()
    {
        // The first version shared one window across every thread: thread B finishing first
        // flushed thread A's suppressed progress into B's channel — a structural
        // cross-conversation content leak in the very component the isolation story leans
        // on — and the shared interval starved every thread but one.
        var (responder, inner, _) = Build();

        await responder.ProgressAsync(Message("thread-A"), "A step 1", CancellationToken.None);
        await responder.ProgressAsync(Message("thread-A"), "A step 2 (suppressed)", CancellationToken.None);
        await responder.ProgressAsync(Message("thread-B"), "B step 1", CancellationToken.None);

        // B's own window is fresh: its first progress goes out despite A's recent send.
        Assert.Contains(("progress", "B step 1"), inner.Sent);

        await responder.CompleteAsync(Message("thread-B"), "B done", CancellationToken.None);

        // B's completion must not carry A's suppressed line.
        Assert.DoesNotContain(("progress", "A step 2 (suppressed)"), inner.Sent);

        await responder.CompleteAsync(Message("thread-A"), "A done", CancellationToken.None);
        Assert.Contains(("progress", "A step 2 (suppressed)"), inner.Sent);
    }

    [Fact]
    public async Task A_status_reply_mid_run_does_not_reset_the_throttle()
    {
        // Every gateway path completes — /status, /stop, refusals — and tearing the window
        // down there let a chatty user defeat the throttle entirely: each reply re-opened
        // the interval, and the next progress line went straight out.
        var (responder, inner, _) = Build();

        await responder.ProgressAsync(Message(), "step 1", CancellationToken.None);
        await responder.CompleteAsync(Message(), "status: running", CancellationToken.None);

        await responder.ProgressAsync(Message(), "step 2 (inside the window)", CancellationToken.None);

        Assert.DoesNotContain(("progress", "step 2 (inside the window)"), inner.Sent);
    }
}
