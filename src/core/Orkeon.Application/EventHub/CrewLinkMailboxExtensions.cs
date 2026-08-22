using Orkeon.Domain.EventHub;

namespace Orkeon.Application.EventHub;

/// <summary>
/// Matches a <see cref="CrewLink"/> against a mailbox address. The link itself is Domain data —
/// what a crew declares — while addressing is a transport concern, so the two meet here rather
/// than dragging <see cref="MailboxAddress"/> down into the Domain.
/// </summary>
public static class CrewLinkMailboxExtensions
{
    /// <summary>
    /// Whether <paramref name="link"/> names <paramref name="target"/>, crew or external peer
    /// alike. Links name crews by <c>name:</c> while addresses carry ids, so the caller
    /// resolves the address's crew id to <paramref name="targetCrewName"/> first (via
    /// <see cref="ICrewLinkProvider.NameOf"/>); an unresolvable crew matches nothing. A topic
    /// mailbox never matches: a link authorizes a correspondent, and a topic is not one.
    /// </summary>
    public static bool Matches(this CrewLink link, MailboxAddress target, string? targetCrewName)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(target);

        return target.Kind switch
        {
            MailboxKind.Client => link.TargetsClient
                && string.Equals(link.ClientName, target.ClientName, StringComparison.Ordinal),
            MailboxKind.Agent or MailboxKind.Crew => link.MatchesCrewName(targetCrewName),
            _ => false,
        };
    }
}
