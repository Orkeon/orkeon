using System.Collections.Immutable;

namespace Orkeon.Application.EventHub;

/// <summary>
/// Optional parameters for <c>IEventHub.PostAsync</c> and <c>IEventHub.SendAsync</c> — the
/// mailbox counterpart of <see cref="PublishOptions"/>.
/// </summary>
public sealed record MailboxOptions
{
    /// <summary>
    /// Metadata copied verbatim into the message envelope: <c>receive_message</c> returns it,
    /// and a responder reads it on the request it answers.
    /// </summary>
    public ImmutableDictionary<string, string>? Metadata { get; init; }
}
