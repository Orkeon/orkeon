using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.EventHub;
using Orkeon.Domain.Common;
using Orkeon.Infrastructure.EventHub;

namespace Orkeon.Application.Tests.EventHub;

public sealed class PublishSubscribeTests
{
    private static InMemoryEventHub NewHub(out DefaultEventHubCallerContext caller)
    {
        caller = new DefaultEventHubCallerContext();
        return new InMemoryEventHub(caller, NullLogger<InMemoryEventHub>.Instance);
    }

    [Fact]
    public async System.Threading.Tasks.Task Publish_global_reaches_every_subscriber()
    {
        using var hub = NewHub(out _);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Eager registration: SubscribeAsync is invoked on the test thread, so by the time
        // PublishAsync runs the bucket already contains three subscribers.
        var e1 = hub.SubscribeAsync("orders.created", cts.Token);
        var e2 = hub.SubscribeAsync("orders.created", cts.Token);
        var e3 = hub.SubscribeAsync("orders.created", cts.Token);
        Assert.Equal(3, hub.GetSubscriberCount("orders.created"));

        var received1 = ReceiveOneAsync(e1);
        var received2 = ReceiveOneAsync(e2);
        var received3 = ReceiveOneAsync(e3);

        await hub.PublishAsync("orders.created", new { id = "o-1" }, options: null, CancellationToken.None);

        var msgs = await System.Threading.Tasks.Task.WhenAll(received1, received2, received3);
        Assert.All(msgs, m => Assert.Equal("orders.created", m.Topic));
    }

    [Fact]
    public async System.Threading.Tasks.Task Publish_scoped_to_crew_reaches_only_that_crew_subscribers()
    {
        using var hub = NewHub(out var caller);
        var crewX = CrewId.Create();
        var crewY = CrewId.Create();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        IAsyncEnumerable<Message> ex, ey;
        using (caller.Push(new EventHubCaller(crewX, null)))
            ex = hub.SubscribeAsync("orders.created", cts.Token);
        using (caller.Push(new EventHubCaller(crewY, null)))
            ey = hub.SubscribeAsync("orders.created", cts.Token);

        Assert.Equal(2, hub.GetSubscriberCount("orders.created"));

        var receivedA = ReceiveOneAsync(ex);
        var receivedB = ReceiveOneAsync(ey);

        await hub.PublishAsync(
            "orders.created",
            new { id = "o-1" },
            new PublishOptions { TargetCrewId = crewX },
            CancellationToken.None);

        var msgA = await receivedA;
        Assert.Equal("orders.created", msgA.Topic);

        // Crew Y must NOT receive the scoped message: cancel its subscription and verify.
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => receivedB);
    }

    [Fact]
    public async System.Threading.Tasks.Task Subscribe_fans_out_to_three_subscribers()
    {
        using var hub = NewHub(out _);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var e1 = hub.SubscribeAsync("ping", cts.Token);
        var e2 = hub.SubscribeAsync("ping", cts.Token);
        var e3 = hub.SubscribeAsync("ping", cts.Token);
        Assert.Equal(3, hub.GetSubscriberCount("ping"));

        var rA = ReceiveOneAsync(e1);
        var rB = ReceiveOneAsync(e2);
        var rC = ReceiveOneAsync(e3);

        await hub.PublishAsync("ping", new { n = 42 }, options: null, CancellationToken.None);

        var msgs = await System.Threading.Tasks.Task.WhenAll(rA, rB, rC);
        Assert.Equal(3, msgs.Length);
    }

    [Fact]
    public async System.Threading.Tasks.Task Subscribe_terminates_cleanly_on_cancellation_without_exception()
    {
        using var hub = NewHub(out _);
        using var cts = new CancellationTokenSource();

        var enumerable = hub.SubscribeAsync("never-published", cts.Token);
        var task = System.Threading.Tasks.Task.Run(async () =>
        {
            var count = 0;
            try
            {
                await foreach (var _ in enumerable)
                    count++;
            }
            catch (OperationCanceledException) { /* expected on .NET 10 IAsyncEnumerable */ }
            return count;
        });

        await System.Threading.Tasks.Task.Delay(100, TestContext.Current.CancellationToken);
        await cts.CancelAsync();

        var count = await task;
        Assert.Equal(0, count);
    }

    [Fact]
    public async System.Threading.Tasks.Task Retained_last_value_is_returned_by_GetLastValueAsync()
    {
        using var hub = NewHub(out _);
        var crewScope = CrewId.Create();

        await hub.PublishAsync(
            "config.update",
            new { mode = "rw" },
            new PublishOptions { TargetCrewId = crewScope, RetainAsLastValue = true, LastValueKey = "config" },
            CancellationToken.None);

        var got = await hub.GetLastValueAsync("config", crewScope, CancellationToken.None);
        Assert.NotNull(got);

        var payload = InMemoryEventHub.DeserializePayload<JsonElement>(got!.Payload);
        Assert.Equal("rw", payload.GetProperty("mode").GetString());
    }

    [Fact]
    public async System.Threading.Tasks.Task GetLastValueAsync_returns_null_for_unknown_key()
    {
        using var hub = NewHub(out _);
        var got = await hub.GetLastValueAsync("missing", crewScope: null, CancellationToken.None);
        Assert.Null(got);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static System.Threading.Tasks.Task<Message> ReceiveOneAsync(IAsyncEnumerable<Message> source)
        => System.Threading.Tasks.Task.Run(async () =>
        {
            await foreach (var msg in source)
                return msg;
            throw new InvalidOperationException("Subscription closed without a message.");
        });
}
