using System.Collections.Immutable;

namespace Orkeon.Studio.Core.Configuration;

/// <summary>
/// One e-mail account as <c>Orkeon:Tools:Email:Accounts</c> spells it (STUDIO-65): every key the
/// engine's binder reads, flattened. A field left <see langword="null"/> is a key that is
/// absent — never an empty string, which the engine reads as a value and not as "unset" — so
/// the engine's own default (the provider preset) applies to it. Enumerations are kept as the
/// text the file holds, in the spelling of <see cref="EmailSection"/>; this type validates nothing.
/// </summary>
public sealed record EmailAccountDefinition
{
    /// <summary>The dictionary key under <c>Orkeon:Tools:Email:Accounts</c>; what an agent passes as <c>account</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The preset, one of <see cref="EmailSection.Providers"/>; absent means <c>Custom</c> to the engine.</summary>
    public string? Provider { get; init; }

    /// <summary>The address, also the <c>From</c> of every message the account sends.</summary>
    public string? Address { get; init; }

    /// <summary>The display name paired with <see cref="Address"/>.</summary>
    public string? DisplayName { get; init; }

    /// <summary>What an agent may do. <see cref="EmailRight.None"/> writes no key.</summary>
    public EmailRight Rights { get; init; }

    /// <summary>
    /// The <c>Rights</c> text the file holds when it is not a list of rights the engine reads
    /// (a misspelt name, an undefined number, <c>None</c>), set only while <see cref="Rights"/> is
    /// <see cref="EmailRight.None"/>: it is written back as it was until a right is chosen.
    /// </summary>
    public string? RightsRaw { get; init; }

    /// <summary>Protocol timeout in seconds; IMAP, POP3 and SMTP only.</summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>Whether a sent message is appended to the Sent folder; absent follows the preset.</summary>
    public bool? SaveSentCopy { get; init; }

    /// <summary><c>Incoming:Protocol</c>, one of <see cref="EmailSection.IncomingProtocols"/>.</summary>
    public string? IncomingProtocol { get; init; }

    /// <summary><c>Incoming:Host</c>.</summary>
    public string? IncomingHost { get; init; }

    /// <summary><c>Incoming:Port</c>.</summary>
    public int? IncomingPort { get; init; }

    /// <summary><c>Incoming:Security</c>, one of <see cref="EmailSection.Securities"/>.</summary>
    public string? IncomingSecurity { get; init; }

    /// <summary><c>Outgoing:Protocol</c>, one of <see cref="EmailSection.OutgoingProtocols"/>.</summary>
    public string? OutgoingProtocol { get; init; }

    /// <summary><c>Outgoing:Host</c>; an account without one cannot send.</summary>
    public string? OutgoingHost { get; init; }

    /// <summary><c>Outgoing:Port</c>.</summary>
    public int? OutgoingPort { get; init; }

    /// <summary><c>Outgoing:Security</c>, one of <see cref="EmailSection.Securities"/>.</summary>
    public string? OutgoingSecurity { get; init; }

    /// <summary><c>Auth:Method</c>, one of <see cref="EmailSection.AuthMethods"/>.</summary>
    public string? AuthMethod { get; init; }

    /// <summary><c>Auth:Username</c>; the engine defaults it to the address.</summary>
    public string? Username { get; init; }

    /// <summary><c>Auth:PasswordEnvVar</c>: the <i>name</i> of the variable that holds the password, never the password.</summary>
    public string? PasswordEnvVar { get; init; }

    /// <summary><c>Auth:ClientId</c>: the OAuth client id.</summary>
    public string? ClientId { get; init; }

    /// <summary><c>Auth:ClientSecretEnvVar</c>: the <i>name</i> of the variable that holds the OAuth client secret.</summary>
    public string? ClientSecretEnvVar { get; init; }

    /// <summary><c>Auth:Tenant</c>: a Microsoft tenant alias or identifier.</summary>
    public string? Tenant { get; init; }

    /// <summary><c>Send:AllowedRecipients</c>: an address, <c>*@domain</c> or <c>*</c>; empty means nobody.</summary>
    public ImmutableList<string> AllowedRecipients { get; init; } = [];

    /// <summary><c>Send:MaxRecipients</c>.</summary>
    public int? MaxRecipients { get; init; }

    /// <summary><c>Send:MaxPerHour</c>.</summary>
    public int? MaxPerHour { get; init; }
}
