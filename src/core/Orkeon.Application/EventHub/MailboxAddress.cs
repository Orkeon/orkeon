using Orkeon.Application.EventHub.Exceptions;
using Orkeon.Domain.Common;

namespace Orkeon.Application.EventHub;

/// <summary>
/// Identifies the kind of mailbox addressed by a <see cref="MailboxAddress"/>.
/// </summary>
public enum MailboxKind
{
    /// <summary>Mailbox of a specific agent (<c>agent://{crewId}/{agentId}</c>).</summary>
    Agent,
    /// <summary>Internal router mailbox of a crew (<c>crew://{crewId}</c>).</summary>
    Crew,
    /// <summary>Alias for a broadcast topic (<c>topic://{topicName}</c>).</summary>
    Topic
}

/// <summary>
/// Structured URI identifying a routing destination on the EventHub.
/// Supported schemes: <c>agent://{crewId}/{agentId}</c>, <c>crew://{crewId}</c>, <c>topic://{topicName}</c>.
/// </summary>
public sealed record MailboxAddress
{
    /// <summary>Gets the kind of mailbox.</summary>
    public required MailboxKind Kind { get; init; }

    /// <summary>Gets the original URI representation of the address.</summary>
    public required string Raw { get; init; }

    /// <summary>Gets the crew identifier embedded in the address (only for <see cref="MailboxKind.Agent"/> and <see cref="MailboxKind.Crew"/>).</summary>
    public CrewId? CrewId { get; init; }

    /// <summary>Gets the agent identifier embedded in the address (only for <see cref="MailboxKind.Agent"/>).</summary>
    public AgentId? AgentId { get; init; }

    /// <summary>Gets the topic name embedded in the address (only for <see cref="MailboxKind.Topic"/>).</summary>
    public string? Topic { get; init; }

    private const string AgentScheme = "agent://";
    private const string CrewScheme = "crew://";
    private const string TopicScheme = "topic://";

    /// <summary>
    /// Parses a URI into a <see cref="MailboxAddress"/>. Accepts the three documented schemes.
    /// </summary>
    /// <exception cref="InvalidMailboxAddressException">
    /// Thrown when the input is null/empty, uses an unsupported scheme, or contains malformed identifiers.
    /// </exception>
    public static MailboxAddress Parse(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        // OriginalString preserves the exact input (no scheme/host normalization or trailing
        // slash insertion), so the custom agent://, crew://, topic:// schemes round-trip
        // through the string-based parser unchanged.
        return ParseString(uri.OriginalString);
    }

    private static MailboxAddress ParseString(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            throw new InvalidMailboxAddressException("Mailbox address cannot be empty.", nameof(uri));

        if (uri.StartsWith(AgentScheme, StringComparison.Ordinal))
            return ParseAgent(uri);
        if (uri.StartsWith(CrewScheme, StringComparison.Ordinal))
            return ParseCrew(uri);
        if (uri.StartsWith(TopicScheme, StringComparison.Ordinal))
            return ParseTopic(uri);

        throw new InvalidMailboxAddressException(
            $"Unsupported mailbox scheme in '{uri}'. Expected one of: agent://, crew://, topic://.",
            nameof(uri));
    }

    /// <summary>Attempts to parse a URI into a <see cref="MailboxAddress"/>.</summary>
    public static bool TryParse(Uri uri, out MailboxAddress? address)
    {
        try
        {
            address = Parse(uri);
            return true;
        }
        catch (InvalidMailboxAddressException)
        {
            address = null;
            return false;
        }
    }

    private static MailboxAddress ParseAgent(string uri)
    {
        var path = uri[AgentScheme.Length..];
        var slash = path.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0 || slash >= path.Length - 1)
            throw new InvalidMailboxAddressException(
                $"agent:// address must be of the form agent://{{crewId}}/{{agentId}} — got '{uri}'.", nameof(uri));

        var crewPart = path[..slash];
        var agentPart = path[(slash + 1)..];

        if (agentPart.Contains('/', StringComparison.Ordinal))
            throw new InvalidMailboxAddressException(
                $"agent:// address must contain exactly one '/' separator — got '{uri}'.", nameof(uri));

        CrewId crewId;
        AgentId agentId;
        try { crewId = Domain.Common.CrewId.Parse(crewPart); }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            throw new InvalidMailboxAddressException(
                $"Invalid crewId segment '{crewPart}' in '{uri}'.", nameof(uri));
        }
        try { agentId = Domain.Common.AgentId.Parse(agentPart); }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            throw new InvalidMailboxAddressException(
                $"Invalid agentId segment '{agentPart}' in '{uri}'.", nameof(uri));
        }

        return new MailboxAddress
        {
            Kind = MailboxKind.Agent,
            Raw = uri,
            CrewId = crewId,
            AgentId = agentId
        };
    }

    private static MailboxAddress ParseCrew(string uri)
    {
        var crewPart = uri[CrewScheme.Length..];
        if (string.IsNullOrWhiteSpace(crewPart) || crewPart.Contains('/', StringComparison.Ordinal))
            throw new InvalidMailboxAddressException(
                $"crew:// address must be of the form crew://{{crewId}} — got '{uri}'.", nameof(uri));

        CrewId crewId;
        try { crewId = Domain.Common.CrewId.Parse(crewPart); }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            throw new InvalidMailboxAddressException(
                $"Invalid crewId segment '{crewPart}' in '{uri}'.", nameof(uri));
        }

        return new MailboxAddress
        {
            Kind = MailboxKind.Crew,
            Raw = uri,
            CrewId = crewId
        };
    }

    private static MailboxAddress ParseTopic(string uri)
    {
        var topic = uri[TopicScheme.Length..];
        if (string.IsNullOrWhiteSpace(topic))
            throw new InvalidMailboxAddressException(
                $"topic:// address must be of the form topic://{{topicName}} — got '{uri}'.", nameof(uri));

        if (!IsValidTopicName(topic))
            throw new InvalidMailboxAddressException(
                $"Topic name '{topic}' contains invalid characters. Allowed: letters, digits, '.', '_', '-'.", nameof(uri));

        return new MailboxAddress
        {
            Kind = MailboxKind.Topic,
            Raw = uri,
            Topic = topic
        };
    }

    private static bool IsValidTopicName(string topic)
    {
        foreach (var c in topic)
        {
            if (!(char.IsLetterOrDigit(c) || c is '.' or '_' or '-'))
                return false;
        }
        return true;
    }

    /// <inheritdoc/>
    public override string ToString() => Raw;
}
