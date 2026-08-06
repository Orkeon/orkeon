using Orkeon.Application.Interfaces.Ports;
using Orkeon.Cli.Scripting.Dispatch;
using Orkeon.Cli.Scripting.Progress;

namespace Orkeon.Cli.Scripting.Tests.Progress;

/// <summary>
/// <see cref="LlmRetryProgressObserver"/>: a reconnection backoff must be VISIBLE (status
/// line + instance progress) while it lasts, and leave no stale banner once the call
/// settles — without ever erasing progress that belongs to someone else.
/// </summary>
public sealed class LlmRetryProgressObserverTests
{
    private static CommandInstance NewInstance()
        => new CommandInstanceRegistry().Register(
            "assistant", CommandInstanceKind.Async, "crew:main-loop", "main-loop", Guid.NewGuid());

    private static LlmRetryEvent Retry(int attempt = 4, int max = 10)
        => new()
        {
            Provider = "Kimi",
            Host = "api.moonshot.ai",
            Attempt = attempt,
            MaxRetries = max,
            Delay = TimeSpan.FromSeconds(8),
            Reason = "Resource temporarily unavailable (api.moonshot.ai:443)",
        };

    [Fact]
    public void A_retry_wait_surfaces_on_the_broker_and_the_ambient_instance()
    {
        var broker = new ProgressBroker();
        var observer = new LlmRetryProgressObserver(broker);
        var instance = NewInstance();

        ProgressAmbient.CurrentInstance = instance;
        try { observer.OnRetryScheduled(Retry()); }
        finally { ProgressAmbient.CurrentInstance = null; }

        var snapshot = broker.Current;
        Assert.NotNull(snapshot);
        Assert.Equal("Reconnecting to api.moonshot.ai", snapshot.Label);
        Assert.Contains("retry 4/10", snapshot.Message, StringComparison.Ordinal);
        Assert.Contains("8s", snapshot.Message, StringComparison.Ordinal);
        Assert.Equal(instance.Ticket, snapshot.Ticket);

        var progress = instance.Snapshot().progress;
        Assert.NotNull(progress);
        Assert.Contains("Reconnecting to api.moonshot.ai", progress.message, StringComparison.Ordinal);
    }

    [Fact]
    public void Settling_clears_the_banner_it_raised()
    {
        var broker = new ProgressBroker();
        var observer = new LlmRetryProgressObserver(broker);

        observer.OnRetryScheduled(Retry());
        Assert.NotNull(broker.Current);

        observer.OnCallSettled();
        Assert.Null(broker.Current);
    }

    [Fact]
    public void Settling_without_a_retry_never_clears_someone_elses_progress()
    {
        // Every LLM call settles, retried or not — a no-retry call ending must not erase
        // a crew's own progress bar sitting in the (single) broker slot.
        var broker = new ProgressBroker();
        broker.Report(new ProgressSnapshot { Label = "Indexing codebase", Percent = 40 });

        new LlmRetryProgressObserver(broker).OnCallSettled();

        Assert.NotNull(broker.Current);
        Assert.Equal("Indexing codebase", broker.Current.Label);
    }

    [Fact]
    public void A_newer_publisher_wins_and_settling_leaves_it_alone()
    {
        // The banner was superseded (label differs) → the label-guarded clear is a no-op.
        var broker = new ProgressBroker();
        var observer = new LlmRetryProgressObserver(broker);

        observer.OnRetryScheduled(Retry());
        broker.Report(new ProgressSnapshot { Label = "Compacting conversation", Percent = 10 });

        observer.OnCallSettled();

        Assert.NotNull(broker.Current);
        Assert.Equal("Compacting conversation", broker.Current.Label);
    }

    [Fact]
    public void Without_an_ambient_instance_the_banner_is_unticketed_but_still_shows()
    {
        var broker = new ProgressBroker();
        new LlmRetryProgressObserver(broker).OnRetryScheduled(Retry(attempt: 1, max: 10));

        var snapshot = broker.Current;
        Assert.NotNull(snapshot);
        Assert.Null(snapshot.Ticket);
        Assert.Contains("retry 1/10", snapshot.Message, StringComparison.Ordinal);
    }
}
