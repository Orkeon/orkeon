using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.EventHub;
using Orkeon.Application.EventHub.Exceptions;
using Orkeon.Domain.Common;
using Orkeon.Domain.EventHub;
using Orkeon.Infrastructure.EventHub;
using Orkeon.Infrastructure.EventHub.Middleware;

namespace Orkeon.Infrastructure.Tests.EventHub;

/// <summary>
/// HUB-04 closed a hole HUB-03 had left open: <c>Post</c> and <c>Send</c> never travelled the
/// pipeline, so the ACL never saw mailbox traffic — the one path <c>client://</c> uses, and
/// therefore the exact traffic the guard had been built for. These tests run through the real
/// hub, the real registry and a *pushed caller identity*, because isolation is what hid the
/// hole — twice: the first version of this file used a provider that ignored the sender, and
/// so certified an ACL that was blind to it.
/// </summary>
public class PipelineOnMailboxTrafficTests
{
    private sealed class CountingMiddleware : IEventHubMiddleware
    {
        public int Published { get; private set; }
        public int Received { get; private set; }
        public Message? LastPublished { get; private set; }

        public Task<Message> OnPublishAsync(Message message, Func<Message, Task<Message>> nextHandler, CancellationToken ct)
        {
            Published++;
            LastPublished = message;
            return nextHandler(message);
        }

        public Task<Message> OnReceiveAsync(Message message, Func<Message, Task<Message>> nextHandler, CancellationToken ct)
        {
            Received++;
            return nextHandler(message);
        }
    }

    private static InMemoryEventHub Build(
        DefaultEventHubCallerContext caller, params IEventHubMiddleware[] middlewares) =>
        new(caller, NullLogger<InMemoryEventHub>.Instance, middlewares);

    private static (DefaultEventHubCallerContext Caller, InMemoryCrewLinkRegistry Registry, CrewId Billing) World()
    {
        var billing = CrewId.Create();
        var registry = new InMemoryCrewLinkRegistry();
        registry.Register(billing, "billing", [new CrewLink { To = CrewLink.ForClient("studio") }]);
        return (new DefaultEventHubCallerContext(), registry, billing);
    }

    [Fact]
    public async Task The_ACL_guards_a_post_to_a_client_mailbox_from_a_real_crew()
    {
        var (caller, registry, billing) = World();
        using var hub = Build(caller, new AclEventHubMiddleware(registry));

        var studio = MailboxAddress.Parse(new Uri("client://studio"));
        var stranger = MailboxAddress.Parse(new Uri("client://someone-else"));
        hub.RegisterMailbox(studio);
        hub.RegisterMailbox(stranger);

        using (caller.Push(new EventHubCaller(billing, null)))
        {
            await hub.PostAsync(studio, new { ok = true }, TestContext.Current.CancellationToken);

            // Same crew, an address no link names: declaring one link closed the door.
            await Assert.ThrowsAsync<EventAclException>(
                () => hub.PostAsync(stranger, new { ok = true }, TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task The_hub_stamps_the_pushed_identity_on_the_message()
    {
        // The ACL reads Message.SourceCrewId, and the hub stamps it from the ambient caller.
        // Without a push everything is CrewId.System — the state HUB-03 shipped in, where the
        // ACL was blind to every real sender.
        var (caller, _, billing) = World();
        var counter = new CountingMiddleware();
        using var hub = Build(caller, counter);

        var studio = MailboxAddress.Parse(new Uri("client://studio"));
        hub.RegisterMailbox(studio);

        using (caller.Push(new EventHubCaller(billing, null)))
            await hub.PostAsync(studio, new { ok = true }, TestContext.Current.CancellationToken);

        Assert.NotNull(counter.LastPublished);
        Assert.Equal(billing, counter.LastPublished!.SourceCrewId);
    }

    [Fact]
    public async Task A_refused_post_never_reaches_the_mailbox()
    {
        var (caller, registry, billing) = World();
        using var hub = Build(caller, new AclEventHubMiddleware(registry));

        var stranger = MailboxAddress.Parse(new Uri("client://someone-else"));
        hub.RegisterMailbox(stranger);

        using (caller.Push(new EventHubCaller(billing, null)))
        {
            await Assert.ThrowsAsync<EventAclException>(
                () => hub.PostAsync(stranger, new { ok = true }, TestContext.Current.CancellationToken));
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var waited = await hub.WaitForAsync(
            new WaitOnMailbox(stranger),
            new FiniteWaitTimeout(TimeSpan.FromMilliseconds(100)),
            cts.Token);

        Assert.Equal(WaitTimedOutMessageFactory.ReservedTopic, waited.Topic);
    }

    [Fact]
    public async Task Both_sides_of_the_pipeline_run_on_mailbox_traffic()
    {
        var caller = new DefaultEventHubCallerContext();
        var counter = new CountingMiddleware();
        using var hub = Build(caller, counter);

        var mailbox = MailboxAddress.Parse(new Uri("client://studio"));
        hub.RegisterMailbox(mailbox);

        await hub.PostAsync(mailbox, new { ok = true }, TestContext.Current.CancellationToken);
        await hub.WaitForAsync(
            new WaitOnMailbox(mailbox),
            new FiniteWaitTimeout(TimeSpan.FromSeconds(2)),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, counter.Published);
        Assert.Equal(1, counter.Received);
    }

    [Fact]
    public async Task A_reply_travels_the_publish_pipeline_too()
    {
        // reply_to and the client bridge both reach ReplyAsync from outside the process; a
        // reply that skipped the stages would be the one hub message nobody logs, spans or
        // checks. §12.1 exempts only the receive half of awaiting a reply — not this.
        var caller = new DefaultEventHubCallerContext();
        var counter = new CountingMiddleware();
        using var hub = Build(caller, counter);

        var mailbox = MailboxAddress.Parse(new Uri("client://studio"));
        using var registration = hub.RegisterMailbox(mailbox) as IDisposable;

        var send = hub.SendAsync<object, object>(
            mailbox, new { ask = true }, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var request = await hub.WaitForAsync(
            new WaitOnMailbox(mailbox),
            new FiniteWaitTimeout(TimeSpan.FromSeconds(2)),
            TestContext.Current.CancellationToken);

        var publishedBeforeReply = counter.Published;
        await hub.ReplyAsync(request.CorrelationId!, new { ok = true }, TestContext.Current.CancellationToken);
        await send;

        Assert.Equal(publishedBeforeReply + 1, counter.Published);
    }

    [Fact]
    public async Task A_published_message_still_reaches_every_subscriber_with_idempotency_on()
    {
        // The regression this pairing could cause: dedup by identifier plus fan-out equals a
        // hub that delivers to exactly one subscriber. Through the real hub, not the stage.
        var caller = new DefaultEventHubCallerContext();
        using var hub = Build(caller, new IdempotencyEventHubMiddleware());

        var first = hub.SubscribeAsync("news", TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var second = hub.SubscribeAsync("news", TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        try
        {
            await hub.PublishAsync("news", new { headline = "x" }, null, TestContext.Current.CancellationToken);

            Assert.True(await first.MoveNextAsync());
            Assert.True(await second.MoveNextAsync());
            Assert.Equal(first.Current.Id, second.Current.Id);
        }
        finally
        {
            await first.DisposeAsync();
            await second.DisposeAsync();
        }
    }

    private sealed class UnregisterDuringPublishMiddleware(Action unregister) : IEventHubMiddleware
    {
        public Task<Message> OnPublishAsync(Message message, Func<Message, Task<Message>> nextHandler, CancellationToken ct)
        {
            unregister();
            return nextHandler(message);
        }

        public Task<Message> OnReceiveAsync(Message message, Func<Message, Task<Message>> nextHandler, CancellationToken ct)
            => nextHandler(message);
    }

    [Fact]
    public async Task A_mailbox_unregistered_during_the_pipeline_is_a_loud_failure()
    {
        // The pipeline awaits, so the mailbox checked before it can be gone after it. The
        // caller must hear that — a TryWrite into a completed channel, silently dropped, is a
        // message the caller was told was posted and that nobody will ever read.
        var caller = new DefaultEventHubCallerContext();
        var mailbox = MailboxAddress.Parse(new Uri("client://studio"));

        IDisposable? registration = null;
        using var hub = Build(caller, new UnregisterDuringPublishMiddleware(() => registration?.Dispose()));
        registration = hub.RegisterMailbox(mailbox);

        await Assert.ThrowsAsync<MailboxNotFoundException>(
            () => hub.PostAsync(mailbox, new { ok = true }, TestContext.Current.CancellationToken));
    }
}
