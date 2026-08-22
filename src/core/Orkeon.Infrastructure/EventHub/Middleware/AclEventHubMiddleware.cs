using Orkeon.Application.EventHub;
using Orkeon.Domain.EventHub;
using Orkeon.Application.EventHub.Exceptions;

namespace Orkeon.Infrastructure.EventHub.Middleware;

/// <summary>
/// Enforces <see cref="CrewLink"/> on the sender side (HUB-03, spec §10.2). This is the stage
/// that lets rc.2 open a door out of the process: BUS-05 gives an external client a mailbox,
/// and a mailbox nobody guards is not a feature.
/// <para>
/// Four rules, each with a reason:
/// </para>
/// <list type="bullet">
/// <item><description>
/// A message with **no target** — a global publish, or a post to a <c>topic://</c> alias —
/// travels freely. The spec says so, and it is coherent: a broadcast is an offer, not a
/// delivery, and subscribers filter on their own side.
/// </description></item>
/// <item><description>
/// A crew that **never declared** a <c>links:</c> block keeps sending, unless the deployment
/// says otherwise (<see cref="ICrewLinkPolicy"/>). The hub shipped with no ACL whatsoever, so
/// refusing undeclared traffic by default would break every existing crew the day this stage
/// is switched on. Under the restrictive policy, an undeclared sender still gets through when
/// the *target* crew granted it an <c>inbound</c> link — that is the grant half of §10.3.
/// </description></item>
/// <item><description>
/// A crew that **did** declare links is held to them — a declared-but-empty block included,
/// because a block whose entries all failed to parse must close the door, not open it.
/// Declaring is the act that closes the door, which is what makes the grammar worth writing.
/// </description></item>
/// <item><description>
/// <c>allowed_topics</c> constrains **topics** only. Point-to-point mail carries a synthetic
/// hub topic no author could name, so it is authorized by the link itself: direction and
/// target.
/// </description></item>
/// </list>
/// </summary>
internal sealed class AclEventHubMiddleware : IEventHubMiddleware
{
    private readonly ICrewLinkProvider _links;
    private readonly ICrewLinkPolicy _policy;

    /// <summary>Builds the stage over the link source and the undeclared-traffic policy.</summary>
    public AclEventHubMiddleware(ICrewLinkProvider links, ICrewLinkPolicy? policy = null)
    {
        _links = links ?? throw new ArgumentNullException(nameof(links));
        _policy = policy ?? PermissiveCrewLinkPolicy.Instance;
    }

    /// <inheritdoc />
    public Task<Message> OnPublishAsync(Message message, Func<Message, Task<Message>> nextHandler, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(nextHandler);

        Authorize(message);
        return nextHandler(message);
    }

    /// <inheritdoc />
    public Task<Message> OnReceiveAsync(Message message, Func<Message, Task<Message>> nextHandler, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(nextHandler);

        // Authorization happens once, on the sender side (spec §10.3) — the inbound grant
        // included, since the sender-side check already consults the target's declarations.
        // Checking again here would double-refuse a message that already passed.
        return nextHandler(message);
    }

    private void Authorize(Message message)
    {
        // A global publish carries no target, and a topic:// mailbox is a broadcast alias:
        // nothing to authorize against — subscribers filter on their own side.
        if (message.TargetCrewId is null && message.TargetMailbox is null)
            return;
        if (message.TargetMailbox is { Kind: MailboxKind.Topic })
            return;

        var declared = _links.LinksFor(message.SourceCrewId);
        if (declared.IsDefault)
        {
            // Never declared. The policy arbitrates — and under the closed policy, the
            // target's own declarations can still grant this sender an inbound link.
            if (_policy.AllowsUndeclared || TargetGrantsInbound(message))
                return;

            throw new EventAclException(
                $"Crew '{message.SourceCrewId}' declared no CrewLink, the deployment refuses "
                + $"undeclared traffic, and the target grants it nothing; "
                + $"'{Describe(message)}' was not delivered.");
        }

        // Declared — possibly empty, which closes the door on everything.
        var targetCrewName = ResolveTargetCrewName(message);
        foreach (var link in declared)
        {
            if (!link.AllowsOutbound)
                continue;

            if (message.TargetMailbox is { } mailbox)
            {
                // Point-to-point mail: the link itself authorizes; its topic list constrains
                // real topics, not the hub's synthetic ones.
                if (link.Matches(mailbox, targetCrewName))
                    return;
                continue;
            }

            // Crew-scoped publish: name and topic must both be authorized.
            if (link.MatchesCrewName(targetCrewName) && link.Authorizes(message.Topic))
                return;
        }

        throw new EventAclException(
            $"No CrewLink of crew '{message.SourceCrewId}' authorizes '{Describe(message)}'.");
    }

    /// <summary>
    /// The §10.3 grant: whether the *target* crew declared a link naming the sender with an
    /// inbound (or bidirectional) direction. Only crews can grant — an external peer declares
    /// nothing — and only a sender the registry can name can be granted anything.
    /// </summary>
    private bool TargetGrantsInbound(Message message)
    {
        var targetCrewId = message.TargetCrewId ?? message.TargetMailbox?.CrewId;
        if (targetCrewId is null)
            return false;

        var targetLinks = _links.LinksFor(targetCrewId);
        if (targetLinks.IsDefaultOrEmpty)
            return false;

        var senderName = _links.NameOf(message.SourceCrewId);
        if (senderName is null)
            return false;

        foreach (var link in targetLinks)
        {
            if (!link.GrantsInbound || !link.MatchesCrewName(senderName))
                continue;
            if (message.TargetMailbox is not null || link.Authorizes(message.Topic))
                return true;
        }

        return false;
    }

    private string? ResolveTargetCrewName(Message message)
    {
        var targetCrewId = message.TargetMailbox?.CrewId ?? message.TargetCrewId;
        return targetCrewId is null ? null : _links.NameOf(targetCrewId);
    }

    private static string Describe(Message message) =>
        message.TargetMailbox is { } mailbox
            ? $"{message.Topic} → {mailbox.Raw}"
            : $"{message.Topic} → crew {message.TargetCrewId}";
}
