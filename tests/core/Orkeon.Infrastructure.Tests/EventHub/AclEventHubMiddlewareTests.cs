using System.Collections.Immutable;
using Orkeon.Application.EventHub;
using Orkeon.Application.EventHub.Exceptions;
using Orkeon.Domain.Common;
using Orkeon.Domain.EventHub;
using Orkeon.Infrastructure.EventHub.Middleware;

namespace Orkeon.Infrastructure.Tests.EventHub;

/// <summary>
/// Serves a fixed set of links and names, so the ACL can be driven without any YAML. Mirrors
/// the registry's contract: <c>default</c> for a crew that never declared, an id → name map
/// because links name crews by <c>name:</c> while messages carry ids.
/// </summary>
internal sealed class StubCrewLinkProvider : ICrewLinkProvider
{
    private readonly Dictionary<string, ImmutableArray<CrewLink>> _links = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);

    public StubCrewLinkProvider Declare(CrewId crew, string name, params CrewLink[] links)
    {
        _links[crew.ToString()] = [.. links];
        _names[crew.ToString()] = name;
        return this;
    }

    /// <summary>A crew the registry knows by name but that declared no links: block.</summary>
    public StubCrewLinkProvider Know(CrewId crew, string name)
    {
        _names[crew.ToString()] = name;
        return this;
    }

    public ImmutableArray<CrewLink> LinksFor(CrewId source) =>
        _links.TryGetValue(source.ToString(), out var links) ? links : default;

    public string? NameOf(CrewId crew) =>
        _names.TryGetValue(crew.ToString(), out var name) ? name : null;
}

/// <summary>
/// The ACL stage (HUB-03, spec §10). This is what lets rc.2 open a door out of the process:
/// BUS-05 gives an external client a mailbox, and a mailbox nobody guards is not a feature.
/// Links name crews the way authors write them — by <c>name:</c> — and the stage resolves the
/// ids a message carries back to those names.
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

    private static StubCrewLinkProvider World() => new StubCrewLinkProvider()
        .Know(Billing, "billing").Know(Fraud, "fraud").Know(Audit, "audit");

    [Fact]
    public async Task A_global_publish_travels_freely()
    {
        // No target means nothing to authorize against: a global publish is an offer, and
        // subscribers filter on their own side (spec §10.2).
        var acl = new AclEventHubMiddleware(World(), RestrictiveCrewLinkPolicy.Instance);

        var message = await Run(acl, Message("anything", Billing));

        Assert.Equal("anything", message.Topic);
    }

    [Fact]
    public async Task A_topic_mailbox_is_a_broadcast_and_travels_like_one()
    {
        // topic:// is an alias for a broadcast. Refusing it for a crew that declared links
        // would silently forfeit topic posting the moment the door closes on correspondents —
        // a link authorizes a correspondent, and a topic is not one.
        var links = World().Declare(Billing, "billing", new CrewLink { To = "fraud" });
        var acl = new AclEventHubMiddleware(links, RestrictiveCrewLinkPolicy.Instance);

        var message = await Run(acl, Message("t", Billing, mailbox: MailboxAddress.Parse(new Uri("topic://updates"))));

        Assert.Equal("t", message.Topic);
    }

    [Fact]
    public async Task A_crew_that_never_declared_keeps_sending_by_default()
    {
        // The hub shipped with no ACL at all. Refusing undeclared traffic the day this stage
        // is switched on would break every existing crew, so the permissive policy is the
        // default and declaring a link is what closes the door.
        var acl = new AclEventHubMiddleware(World());

        var message = await Run(acl, Message("fraud.check", Billing, targetCrew: Fraud));

        Assert.Equal("fraud.check", message.Topic);
    }

    [Fact]
    public async Task A_restrictive_deployment_refuses_undeclared_traffic()
    {
        var acl = new AclEventHubMiddleware(World(), RestrictiveCrewLinkPolicy.Instance);

        var ex = await Assert.ThrowsAsync<EventAclException>(
            () => Run(acl, Message("fraud.check", Billing, targetCrew: Fraud)));

        Assert.Contains("declared no CrewLink", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_targets_inbound_link_grants_an_undeclared_sender_passage()
    {
        // §10.3's second row: `inbound` means "the named peer may send to me". Under the
        // closed policy that grant is the only way an undeclared sender gets through — which
        // is exactly what makes the keyword worth writing.
        var links = World().Declare(
            Fraud, "fraud",
            new CrewLink { To = "billing", Direction = CrewLinkDirection.Inbound, AllowedTopics = ["fraud.check"] });
        var acl = new AclEventHubMiddleware(links, RestrictiveCrewLinkPolicy.Instance);

        // Granted: fraud declared that billing may write to it, on this topic.
        await Run(acl, Message("fraud.check", Billing, targetCrew: Fraud));

        // The grant is topic-scoped like any other authorization.
        await Assert.ThrowsAsync<EventAclException>(
            () => Run(acl, Message("fraud.secret", Billing, targetCrew: Fraud)));

        // And it names billing alone — audit gets nothing from it.
        await Assert.ThrowsAsync<EventAclException>(
            () => Run(acl, Message("fraud.check", Audit, targetCrew: Fraud)));
    }

    [Fact]
    public async Task A_senders_own_declaration_is_not_reopened_by_the_targets_grant()
    {
        // Declaring links is the act that closes the door. If billing declared links that do
        // not name fraud, fraud's inbound grant must not override billing's own decision.
        var links = World()
            .Declare(Billing, "billing", new CrewLink { To = "audit" })
            .Declare(Fraud, "fraud", new CrewLink { To = "billing", Direction = CrewLinkDirection.Inbound });
        var acl = new AclEventHubMiddleware(links);

        await Assert.ThrowsAsync<EventAclException>(
            () => Run(acl, Message("fraud.check", Billing, targetCrew: Fraud)));
    }

    [Fact]
    public async Task A_declared_but_empty_block_closes_the_door_on_everything()
    {
        // Empty-after-parsing is what a malformed links: block collapses to. It must refuse,
        // not fall back to "undeclared": a malformed authorization must never become a
        // permissive one — even under the permissive policy.
        var links = World().Declare(Billing, "billing");
        var acl = new AclEventHubMiddleware(links);

        await Assert.ThrowsAsync<EventAclException>(
            () => Run(acl, Message("fraud.check", Billing, targetCrew: Fraud)));
    }

    [Fact]
    public async Task A_declared_link_authorizes_its_direction_and_its_topics_only()
    {
        var links = World().Declare(
            Billing, "billing",
            new CrewLink { To = "fraud", Direction = CrewLinkDirection.Bidirectional, AllowedTopics = ["fraud.check"] },
            new CrewLink { To = "audit", Direction = CrewLinkDirection.Inbound, AllowedTopics = ["audit.event"] });
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
        var links = World().Declare(
            Billing, "billing", new CrewLink { To = "fraud", Direction = CrewLinkDirection.Outbound });
        var acl = new AclEventHubMiddleware(links);

        await Run(acl, Message("anything.at.all", Billing, targetCrew: Fraud));
    }

    [Fact]
    public async Task A_topic_list_constrains_topics_not_the_mail()
    {
        // Post/Send carry a synthetic hub topic no author could name. A link that lists
        // topics must still let the mail through — otherwise the only working configuration
        // would be total trust, the opposite of what a topic list is for.
        var fraudAgent = MailboxAddress.Parse(new Uri($"agent://{Fraud}/{AgentId.Create()}"));
        var links = World().Declare(
            Billing, "billing",
            new CrewLink { To = "fraud", AllowedTopics = ["fraud.check"] });
        var acl = new AclEventHubMiddleware(links);

        var message = await Run(acl, Message("_mailbox.post", Billing, mailbox: fraudAgent));

        Assert.Equal("_mailbox.post", message.Topic);
    }

    [Fact]
    public async Task The_yaml_of_the_spec_authorizes_what_it_says_it_does()
    {
        // The doc's own example, verbatim: links written against crew *names*. This is the
        // test that pins the name-vs-id contract — a link matched against a ULID would refuse
        // everything an author ever declared.
        var links = World().Declare(
            Billing, "billing-crew",
            new CrewLink { To = "fraud-crew", Direction = CrewLinkDirection.Bidirectional, AllowedTopics = ["fraud.check", "fraud.result"] })
            .Know(Fraud, "fraud-crew");
        var acl = new AclEventHubMiddleware(links);

        await Run(acl, Message("fraud.check", Billing, targetCrew: Fraud));
    }

    [Fact]
    public async Task An_unresolvable_target_matches_no_link()
    {
        // A crew the registry has never seen has no name, so no link can name it. For a
        // declared sender that is a refusal — you cannot authorize what is not modelled.
        var links = World().Declare(Billing, "billing", new CrewLink { To = "ghost" });
        var acl = new AclEventHubMiddleware(links);

        await Assert.ThrowsAsync<EventAclException>(
            () => Run(acl, Message("t", Billing, targetCrew: CrewId.Create())));
    }

    [Fact]
    public async Task The_external_peer_is_guarded_like_any_other_target()
    {
        // The extension HUB-03 makes to the spec: client:// is not a crew, so CrewLink has
        // to be able to name it — otherwise the external peer would escape the ACL by
        // simply not being modelled.
        var studio = MailboxAddress.Parse(new Uri("client://studio"));
        var links = World().Declare(
            Billing, "billing",
            new CrewLink { To = CrewLink.ForClient("studio"), Direction = CrewLinkDirection.Bidirectional });
        var acl = new AclEventHubMiddleware(links);

        await Run(acl, Message("run.progress", Billing, mailbox: studio));

        // A different peer name is a different peer.
        await Assert.ThrowsAsync<EventAclException>(
            () => Run(acl, Message("run.progress", Billing, mailbox: MailboxAddress.Parse(new Uri("client://someone-else")))));

        // And a crew with no link to it cannot reach the client either.
        var stranger = new AclEventHubMiddleware(World().Declare(
            Fraud, "fraud", new CrewLink { To = "audit" }));
        await Assert.ThrowsAsync<EventAclException>(
            () => Run(stranger, Message("run.progress", Fraud, mailbox: studio)));
    }

    [Fact]
    public async Task The_receive_path_does_not_authorize_twice()
    {
        // Authorization happens once, on the sender side (§10.3). Re-checking on receive
        // would refuse a message that already passed.
        var acl = new AclEventHubMiddleware(World(), RestrictiveCrewLinkPolicy.Instance);

        var message = await acl.OnReceiveAsync(
            Message("fraud.check", Billing, targetCrew: Fraud), Pass, CancellationToken.None);

        Assert.Equal("fraud.check", message.Topic);
    }

    [Fact]
    public async Task The_process_itself_is_not_a_crew_the_ACL_models()
    {
        // System-sourced traffic — the host's own code, the client bridge relaying its peer —
        // cannot be named by any link, so under the closed policy it could be neither
        // authorized nor granted: the daemon's own gateway would have refused itself.
        var acl = new AclEventHubMiddleware(World(), RestrictiveCrewLinkPolicy.Instance);
        var studio = MailboxAddress.Parse(new Uri("client://studio"));

        var message = await Run(acl, Message("_mailbox.post", CrewId.System, mailbox: studio));

        Assert.Equal("_mailbox.post", message.Topic);
    }
}
