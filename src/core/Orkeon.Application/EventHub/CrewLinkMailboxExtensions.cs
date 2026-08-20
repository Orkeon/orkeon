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
    /// alike. A topic mailbox never matches: a link authorizes a correspondent, and a topic is
    /// not one.
    /// </summary>
    public static bool Matches(this CrewLink link, MailboxAddress target)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(target);

        return target.Kind switch
        {
            MailboxKind.Client => link.TargetsClient
                && string.Equals(link.ClientName, target.ClientName, StringComparison.Ordinal),
            MailboxKind.Agent or MailboxKind.Crew => !link.TargetsClient
                && string.Equals(link.To, target.CrewId?.ToString(), StringComparison.Ordinal),
            _ => false,
        };
    }
}
