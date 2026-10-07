using Orkeon.Studio.Core.Validation;

namespace Orkeon.Studio.Core.Configuration;

/// <summary>
/// What the engine makes of an e-mail account once the provider preset has filled what the file
/// leaves out (STUDIO-66): the value a field left blank is worth, which a form shows without
/// writing it — port 993, <c>imap.gmail.com</c>, OAuth2 for Outlook. Enumerations are in the
/// spelling of <see cref="EmailSection"/>. The presets are the engine's, spelt again in
/// <see cref="EmailAccountRules"/> and held to its resolver by a test.
/// </summary>
public sealed record EmailAccountEffective
{
    /// <summary>How mail is read: <c>Graph</c> for Outlook, else <c>Imap</c>, unless the account says.</summary>
    public required string IncomingProtocol { get; init; }

    /// <summary>The server mail is read from: the account's, else the preset's; null through Graph, and for a Custom account that names none.</summary>
    public string? IncomingHost { get; init; }

    /// <summary>The port mail is read on: the account's, else the conventional one of the protocol and the security; null through Graph.</summary>
    public int? IncomingPort { get; init; }

    /// <summary>The security mail is read with: <c>SslOnConnect</c> unless the account says; null through Graph.</summary>
    public string? IncomingSecurity { get; init; }

    /// <summary>
    /// How mail is sent: <c>Graph</c> when it is read through Graph, else <c>Smtp</c> unless the
    /// account says. An account sends only when <see cref="CanSend"/>.
    /// </summary>
    public required string OutgoingProtocol { get; init; }

    /// <summary>The SMTP server: the account's, else the preset's; null through Graph, and for a Custom account that names none.</summary>
    public string? OutgoingHost { get; init; }

    /// <summary>
    /// The SMTP port: the account's, else 465 under <c>SslOnConnect</c> and 587 otherwise; null through
    /// Graph. Given without a host too: it is what the field is worth once a host is named.
    /// </summary>
    public int? OutgoingPort { get; init; }

    /// <summary>The SMTP security: <c>StartTls</c> for Outlook, else <c>SslOnConnect</c>, unless the account says; null through Graph.</summary>
    public string? OutgoingSecurity { get; init; }

    /// <summary>How the account signs in: <c>OAuth2</c> when it names a client or uses the Outlook preset, else <c>Password</c>, unless the account says.</summary>
    public required string AuthMethod { get; init; }

    /// <summary>The login name: the account's, else its address; null when it has neither.</summary>
    public string? Username { get; init; }

    /// <summary>The Microsoft tenant of an Outlook account: its own, else <c>consumers</c>; null for another preset.</summary>
    public string? Tenant { get; init; }

    /// <summary>
    /// Whether a sent message is appended to the Sent folder: the account's choice, else true for a
    /// Custom account that sends through SMTP and does not read through POP3.
    /// </summary>
    public required bool SaveSentCopy { get; init; }

    /// <summary>Whether an outgoing server exists: Graph, or an SMTP host. Without one the engine refuses the right to send.</summary>
    public required bool CanSend { get; init; }

    /// <summary>
    /// The effective values of <paramref name="definition"/>. A value the engine cannot read (a
    /// protocol it does not know) leaves the preset's, as an absent one does; an empty host is a
    /// value, and names no server.
    /// </summary>
    public static EmailAccountEffective Of(EmailAccountDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var provider = EmailAccountRules.Member(EmailSection.Providers, definition.Provider) ?? EmailSection.Values.Custom;
        var outlook = provider == EmailSection.Values.Outlook;
        var incoming = EmailAccountRules.Member(EmailSection.IncomingProtocols, definition.IncomingProtocol)
            ?? (outlook ? EmailSection.Values.Graph : EmailSection.Values.Imap);
        var method = EmailAccountRules.Member(EmailSection.AuthMethods, definition.AuthMethod)
            ?? (!string.IsNullOrWhiteSpace(definition.ClientId) || outlook ? EmailSection.Values.OAuth2 : EmailSection.Values.Password);
        var username = string.IsNullOrWhiteSpace(definition.Username)
            ? EmailAccountRules.MailboxOf(definition.Address)
            : definition.Username.Trim();
        var tenant = !outlook ? null
            : string.IsNullOrWhiteSpace(definition.Tenant) ? EmailAccountRules.DefaultTenant
            : definition.Tenant.Trim();

        if (incoming == EmailSection.Values.Graph)
        {
            // Read through Graph, sent through Graph: no server, port or security on either side.
            return new EmailAccountEffective
            {
                IncomingProtocol = incoming,
                OutgoingProtocol = EmailSection.Values.Graph,
                AuthMethod = method,
                Username = username,
                Tenant = tenant,
                SaveSentCopy = definition.SaveSentCopy ?? false,
                CanSend = true,
            };
        }

        var incomingSecurity = EmailAccountRules.Member(EmailSection.Securities, definition.IncomingSecurity) ?? EmailSection.Values.SslOnConnect;
        var smtp = EmailAccountRules.Member(EmailSection.OutgoingProtocols, definition.OutgoingProtocol) != EmailSection.Values.Graph;
        var outgoingHost = smtp ? Host(definition.OutgoingHost, EmailAccountRules.PresetSmtpHost(provider)) : null;
        var outgoingSecurity = EmailAccountRules.Member(EmailSection.Securities, definition.OutgoingSecurity)
            ?? (outlook ? EmailSection.Values.StartTls : EmailSection.Values.SslOnConnect);
        var canSend = outgoingHost is not null;

        return new EmailAccountEffective
        {
            IncomingProtocol = incoming,
            IncomingHost = Host(definition.IncomingHost, EmailAccountRules.PresetIncomingHost(provider, incoming)),
            IncomingPort = definition.IncomingPort ?? EmailAccountRules.DefaultIncomingPort(incoming, incomingSecurity),
            IncomingSecurity = incomingSecurity,
            OutgoingProtocol = smtp ? EmailSection.Values.Smtp : EmailSection.Values.Graph,
            OutgoingHost = outgoingHost,
            OutgoingPort = smtp ? definition.OutgoingPort ?? EmailAccountRules.DefaultSmtpPort(outgoingSecurity) : null,
            OutgoingSecurity = smtp ? outgoingSecurity : null,
            AuthMethod = method,
            Username = username,
            Tenant = tenant,
            // A custom SMTP server usually files nothing; a POP3 mailbox has no Sent folder to file into.
            SaveSentCopy = definition.SaveSentCopy
                ?? (provider == EmailSection.Values.Custom && canSend && incoming != EmailSection.Values.Pop3),
            CanSend = canSend,
        };
    }

    /// <summary>The host the account declares, trimmed, else the preset's; an empty one is a value and names no server.</summary>
    private static string? Host(string? declared, string? preset) =>
        (declared?.Trim() ?? preset) is { Length: > 0 } host ? host : null;
}
