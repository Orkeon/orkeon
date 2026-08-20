using Orkeon.Application.EventHub;
using Orkeon.Application.EventHub.Exceptions;

namespace Orkeon.Infrastructure.EventHub.Middleware;

/// <summary>
/// Enforces <see cref="CrewLink"/> on the sender side (HUB-03, spec §10.2). This is the stage
/// that lets rc.2 open a door out of the process: BUS-05 gives an external client a mailbox,
/// and a mailbox nobody guards is not a feature.
/// <para>
/// Three rules, each with a reason:
/// </para>
/// <list type="bullet">
/// <item><description>
/// A message with **no target** — a global publish — travels freely. The spec says so, and it
/// is coherent: a global publish is an offer, not a delivery, and subscribers filter on their
/// own side.
/// </description></item>
/// <item><description>
/// A crew that declared **no link at all** keeps sending, unless the deployment says
/// otherwise (<see cref="ICrewLinkPolicy"/>). The hub shipped with no ACL whatsoever, so
/// refusing undeclared traffic by default would break every existing crew the day this stage
/// is switched on.
/// </description></item>
/// <item><description>
/// A crew that **did** declare links is held to them. Declaring one link is the act that
/// closes the door — which is what makes the grammar worth writing.
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

        // Authorization happens once, on the sender side (spec §10.3). Checking again here
        // would double-refuse a message that already passed, and would refuse an inbound
        // message whose sender we do not model.
        return nextHandler(message);
    }

    private void Authorize(Message message)
    {
        // A global publish carries no target: nothing to authorize against.
        if (message.TargetCrewId is null && message.TargetMailbox is null)
            return;

        var declared = _links.LinksFor(message.SourceCrewId);
        if (declared.IsDefaultOrEmpty)
        {
            if (_policy.AllowsUndeclared)
                return;

            throw new EventAclException(
                $"Crew '{message.SourceCrewId}' declared no CrewLink and the deployment refuses "
                + $"undeclared traffic; '{Describe(message)}' was not delivered.");
        }

        foreach (var link in declared)
        {
            if (!link.AllowsOutbound || !link.Authorizes(message.Topic))
                continue;

            var matches = message.TargetMailbox is { } mailbox
                ? link.Matches(mailbox)
                : link.MatchesCrew(message.TargetCrewId!);

            if (matches)
                return;
        }

        throw new EventAclException(
            $"No CrewLink of crew '{message.SourceCrewId}' authorizes '{Describe(message)}'.");
    }

    private static string Describe(Message message) =>
        message.TargetMailbox is { } mailbox
            ? $"{message.Topic} → {mailbox.Raw}"
            : $"{message.Topic} → crew {message.TargetCrewId}";
}
