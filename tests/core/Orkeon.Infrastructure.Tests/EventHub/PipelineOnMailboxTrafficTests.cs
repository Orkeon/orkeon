using System.Collections.Immutable;
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
/// hub rather than the stages in isolation, because isolation is what hid the hole.
/// </summary>
public class PipelineOnMailboxTrafficTests
{
    private sealed class FixedLinkProvider(params CrewLink[] links) : ICrewLinkProvider
    {
        public ImmutableArray<CrewLink> LinksFor(CrewId source) => [.. links];
    }

    private sealed class CountingMiddleware : IEventHubMiddleware
    {
        public int Published { get; private set; }
        public int Received { get; private set; }

        public Task<Message> OnPublishAsync(Message message, Func<Message, Task<Message>> nextHandler, CancellationToken ct)
        {
            Published++;
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

    [Fact]
    public async Task The_ACL_now_guards_a_post_to_a_client_mailbox()
    {
        var caller = new DefaultEventHubCallerContext();
        var acl = new AclEventHubMiddleware(
            new FixedLinkProvider(new CrewLink { To = CrewLink.ForClient("studio") }));
        using var hub = Build(caller, acl);

        var studio = MailboxAddress.Parse(new Uri("client://studio"));
        var stranger = MailboxAddress.Parse(new Uri("client://someone-else"));
        hub.RegisterMailbox(studio);
        hub.RegisterMailbox(stranger);

        await hub.PostAsync(studio, new { ok = true }, TestContext.Current.CancellationToken);

        // Same call, an address no link names: before HUB-04 this went straight through.
        await Assert.ThrowsAsync<EventAclException>(
            () => hub.PostAsync(stranger, new { ok = true }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_refused_post_never_reaches_the_mailbox()
    {
        var caller = new DefaultEventHubCallerContext();
        var acl = new AclEventHubMiddleware(
            new FixedLinkProvider(new CrewLink { To = CrewLink.ForClient("studio") }));
        using var hub = Build(caller, acl);

        var stranger = MailboxAddress.Parse(new Uri("client://someone-else"));
        hub.RegisterMailbox(stranger);

        await Assert.ThrowsAsync<EventAclException>(
            () => hub.PostAsync(stranger, new { ok = true }, TestContext.Current.CancellationToken));

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
}
