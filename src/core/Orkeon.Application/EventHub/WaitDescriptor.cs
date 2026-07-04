using System.Collections.Immutable;

namespace Orkeon.Application.EventHub;

/// <summary>
/// Discriminated union describing the kind of message a <c>WaitForAsync</c> call expects.
/// </summary>
public abstract record WaitDescriptor
{
    private protected WaitDescriptor() { }
}

/// <summary>Wait for the next message on <paramref name="Topic"/>, optionally constrained by metadata match.</summary>
public sealed record WaitOnTopic(
    string Topic,
    ImmutableDictionary<string, string>? MetadataMatch) : WaitDescriptor;

/// <summary>Wait for the next message addressed to <paramref name="Address"/>.</summary>
public sealed record WaitOnMailbox(MailboxAddress Address) : WaitDescriptor;

/// <summary>Wait for the reply to a previously issued request, identified by <paramref name="Correlation"/>.</summary>
public sealed record WaitOnReply(CorrelationId Correlation) : WaitDescriptor;
