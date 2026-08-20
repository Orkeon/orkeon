using System.Collections.Immutable;
using Orkeon.Application.EventHub;
using Orkeon.Application.EventHub.Exceptions;
using Orkeon.Domain.Common;
using Orkeon.Domain.EventHub;
using Orkeon.Infrastructure.EventHub.Middleware;

namespace Orkeon.Infrastructure.Tests.EventHub;

/// <summary>Serves a fixed set of links, so the ACL can be driven without any YAML.</summary>
internal sealed class StubCrewLinkProvider : ICrewLinkProvider
{
    private readonly Dictionary<string, ImmutableArray<CrewLink>> _links = new(StringComparer.Ordinal);

    public StubCrewLinkProvider Declare(CrewId crew, params CrewLink[] links)
    {
        _links[crew.ToString()] = [.. links];
        return this;
    }

    public ImmutableArray<CrewLink> LinksFor(CrewId source) =>
        _links.TryGetValue(source.ToString(), out var links) ? links : [];
}

/// <summary>
/// The ACL stage (HUB-03, spec §10). This is what lets rc.2 open a door out of the process:
/// BUS-05 gives an external client a mailbox, and a mailbox nobody guards is not a feature.
/// </summary>
public class AclEventHubMiddlewareTests
{
    private static readonly CrewId Billing = CrewId.Create();
    private static readonly CrewId Fraud = CrewId.Create();
    private static readonly CrewId Audit = CrewId.Create();

    private static Message Message(string topic, CrewId source, CrewId? targetCrew = null, MailboxAddress? mailbox = null) =>
        new()
        {
            Id = MessageId.NewId(),
            Topic = topic,
            Payload = ReadOnlyMemory<byte>.Empty,
            SchemaId = "s",
            PublishedAt = DateTimeOffset.UnixEpoch,
            SourceCrewId = source,
            TargetCrewId = targetCrew,
            TargetMailbox = mailbox,
            Metadata = ImmutableDictionary<string, string>.Empty,
        };

    private static Task<Message> Pass(Message m) => Task.FromResult(m);

    private static Task<Message> Run(AclEventHubMiddleware acl, Message message) =>
        acl.OnPublishAsync(message, Pass, CancellationToken.None);

    [Fact]
    public async Task A_global_publish_travels_freely()
    {
        // No target means nothing to authorize against: a global publish is an offer, and
        // subscribers filter on their own side (spec §10.2).
        var acl = new AclEventHubMiddleware(new StubCrewLinkProvider(), RestrictiveCrewLinkPolicy.Instance);

        var message = await Run(acl, Message("anything", Billing));

        Assert.Equal("anything", message.Topic);
    }

    [Fact]
    public async Task A_crew_that_declared_nothing_keeps_sending_by_default()
    {
        // The hub shipped with no ACL at all. Refusing undeclared traffic the day this stage
        // is switched on would break every existing crew, so the permissive policy is the
        // default and declaring a link is what closes the door.
        var acl = new AclEventHubMiddleware(new StubCrewLinkProvider());

        var message = await Run(acl, Message("fraud.check", Billing, targetCrew: Fraud));

        Assert.Equal("fraud.check", message.Topic);
    }

    [Fact]
    public async Task A_restrictive_deployment_refuses_undeclared_traffic()
    {
        var acl = new AclEventHubMiddleware(new StubCrewLinkProvider(), RestrictiveCrewLinkPolicy.Instance);

        var ex = await Assert.ThrowsAsync<EventAclException>(
            () => Run(acl, Message("fraud.check", Billing, targetCrew: Fraud)));

        Assert.Contains("declared no CrewLink", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_declared_link_authorizes_its_direction_and_its_topics_only()
    {
        var links = new StubCrewLinkProvider().Declare(
            Billing,
            new CrewLink { To = Fraud.ToString(), Direction = CrewLinkDirection.Bidirectional, AllowedTopics = ["fraud.check"] },
            new CrewLink { To = Audit.ToString(), Direction = CrewLinkDirection.Inbound, AllowedTopics = ["audit.event"] });
        var acl = new AclEventHubMiddleware(links);

        // Authorized: right target, right topic, direction allows outbound.
        await Run(acl, Message("fraud.check", Billing, targetCrew: Fraud));

        // Refused: topic outside the link's list.
        await Assert.ThrowsAsync<EventAclException>(
            () => Run(acl, Message("fraud.secret", Billing, targetCrew: Fraud)));

        // Refused: the audit link is inbound only — B may write to A, not A to B.
        await Assert.ThrowsAsync<EventAclException>(
            () => Run(acl, Message("audit.event", Billing, targetCrew: Audit)));

        // Refused: declaring one link closes the door on every undeclared target.
        await Assert.ThrowsAsync<EventAclException>(
            () => Run(acl, Message("fraud.check", Billing, targetCrew: CrewId.Create())));
    }

    [Fact]
    public async Task A_link_without_a_topic_list_authorizes_every_topic()
    {
        // An empty list is a decision to trust broadly, not an accident: a link that
        // authorized nothing would be pointless.
        var links = new StubCrewLinkProvider().Declare(
            Billing, new CrewLink { To = Fraud.ToString(), Direction = CrewLinkDirection.Outbound });
        var acl = new AclEventHubMiddleware(links);

        await Run(acl, Message("anything.at.all", Billing, targetCrew: Fraud));
    }

    [Fact]
    public async Task The_external_peer_is_guarded_like_any_other_target()
    {
        // The extension HUB-03 makes to the spec: client:// is not a crew, so CrewLink has
        // to be able to name it — otherwise the external peer would escape the ACL by
        // simply not being modelled.
        var studio = MailboxAddress.Parse(new Uri("client://studio"));
        var links = new StubCrewLinkProvider().Declare(
            Billing,
            new CrewLink { To = CrewLink.ForClient("studio"), Direction = CrewLinkDirection.Bidirectional });
        var acl = new AclEventHubMiddleware(links);

        await Run(acl, Message("run.progress", Billing, mailbox: studio));

        // A different peer name is a different peer.
        await Assert.ThrowsAsync<EventAclException>(
            () => Run(acl, Message("run.progress", Billing, mailbox: MailboxAddress.Parse(new Uri("client://someone-else")))));

        // And a crew with no link to it cannot reach the client either.
        var stranger = new AclEventHubMiddleware(new StubCrewLinkProvider().Declare(
            Fraud, new CrewLink { To = Audit.ToString() }));
        await Assert.ThrowsAsync<EventAclException>(
            () => Run(stranger, Message("run.progress", Fraud, mailbox: studio)));
    }

    [Fact]
    public async Task The_receive_path_does_not_authorize_twice()
    {
        // Authorization happens once, on the sender side (§10.3). Re-checking on receive
        // would refuse a message that already passed.
        var acl = new AclEventHubMiddleware(new StubCrewLinkProvider(), RestrictiveCrewLinkPolicy.Instance);

        var message = await acl.OnReceiveAsync(
            Message("fraud.check", Billing, targetCrew: Fraud), Pass, CancellationToken.None);

        Assert.Equal("fraud.check", message.Topic);
    }
}
