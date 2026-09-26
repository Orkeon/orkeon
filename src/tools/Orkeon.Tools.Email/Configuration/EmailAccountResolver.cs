using System.Net;
using MimeKit;
using Orkeon.Tools.Email.Constants;

namespace Orkeon.Tools.Email.Configuration;

/// <summary>The outcome of resolving one declared account.</summary>
/// <param name="Account">The resolved account, or null when <paramref name="Problems"/> is not empty.</param>
/// <param name="Problems">Everything wrong with the declaration, each an actionable sentence.</param>
internal sealed record EmailAccountResolution(ResolvedEmailAccount? Account, IReadOnlyList<string> Problems);

/// <summary>
/// Applies the provider presets to a declared account and validates the result. Every problem
/// is collected, not only the first, so an operator fixes a declaration in one pass.
/// </summary>
internal static class EmailAccountResolver
{
    private const int MaxNameLength = 64;

    /// <summary>Resolves <paramref name="options"/>, declared under <paramref name="name"/>.</summary>
    public static EmailAccountResolution Resolve(string name, EmailAccountOptions options)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(options);

        var problems = new List<string>();
        if (!IsValidName(name))
            problems.Add("the account name may only hold letters, digits, '.', '_' and '-' (64 characters at most)");

        var address = ResolveAddress(options, problems);
        if (options.Rights == EmailRights.None)
            problems.Add("Rights is required and says what an agent may do, e.g. \"Rights\": \"Read, Organize, Draft\"");

        var incoming = ResolveIncoming(options, problems);
        var (outgoing, outgoingEndpoint) = ResolveOutgoing(options, incoming.Protocol, problems);
        var auth = ResolveAuth(options, address, incoming.Protocol, problems);
        var send = ResolveSend(options, problems);
        var timeout = ResolveTimeout(options, problems);

        if (options.Rights.HasFlag(EmailRights.Send) && outgoing is null)
            problems.Add("Rights grant Send but the account declares no outgoing server (Outgoing:Host)");

        if (problems.Count > 0 || address is null || auth is null)
        {
            return new EmailAccountResolution(
                null,
                problems.Select(problem => $"account '{name}': {problem}").ToList());
        }

        var account = new ResolvedEmailAccount
        {
            Name = name,
            Provider = options.Provider,
            Address = address,
            DisplayName = string.IsNullOrWhiteSpace(options.DisplayName) ? null : options.DisplayName.Trim(),
            Rights = options.Rights,
            Incoming = incoming.Protocol,
            IncomingEndpoint = incoming.Endpoint,
            Outgoing = outgoing,
            OutgoingEndpoint = outgoingEndpoint,
            Auth = auth,
            Send = send,
            SaveSentCopy = options.SaveSentCopy ?? (options.Provider == EmailProvider.Custom && outgoing == OutgoingProtocol.Smtp),
            Timeout = timeout,
        };
        return new EmailAccountResolution(account, []);
    }

    /// <summary>
    /// How a declared account signs in: its explicit <c>Auth:Method</c>, else OAuth2 when it names a
    /// client or uses the Outlook preset, else a password.
    /// </summary>
    public static EmailAuthMethod EffectiveAuthMethod(EmailAccountOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Auth.Method
            ?? (!string.IsNullOrWhiteSpace(options.Auth.ClientId) || options.Provider == EmailProvider.Outlook
                ? EmailAuthMethod.OAuth2
                : EmailAuthMethod.Password);
    }

    /// <summary>Whether <paramref name="name"/> is usable as an account name (and a token file stem).</summary>
    public static bool IsValidName(string name) =>
        !string.IsNullOrEmpty(name)
        && name.Length <= MaxNameLength
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    /// <summary>Whether <paramref name="host"/> designates this machine.</summary>
    public static bool IsLoopbackHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip));

    private static string? ResolveAddress(EmailAccountOptions options, List<string> problems)
    {
        var address = options.Address?.Trim();
        if (string.IsNullOrEmpty(address))
        {
            problems.Add("Address is required");
            return null;
        }

        if (!MailboxAddress.TryParse(address, out var mailbox) || !mailbox.Address.Contains('@', StringComparison.Ordinal))
        {
            problems.Add($"Address '{address}' is not an e-mail address");
            return null;
        }

        return mailbox.Address;
    }

    private static (IncomingProtocol Protocol, MailEndpoint? Endpoint) ResolveIncoming(
        EmailAccountOptions options, List<string> problems)
    {
        var declared = options.Incoming;
        var protocol = declared.Protocol
            ?? (options.Provider == EmailProvider.Outlook ? IncomingProtocol.Graph : IncomingProtocol.Imap);

        if (protocol == IncomingProtocol.Graph)
        {
            if (options.Provider != EmailProvider.Outlook)
                problems.Add("Incoming:Protocol Graph is only available with the Outlook preset");
            return (protocol, null);
        }

        var host = declared.Host?.Trim() ?? PresetIncomingHost(options.Provider, protocol);
        if (string.IsNullOrEmpty(host))
        {
            problems.Add("Incoming:Host is required for a Custom account");
            return (protocol, null);
        }

        var security = declared.Security ?? TransportSecurity.SslOnConnect;
        var port = declared.Port ?? DefaultPort(protocol, security);
        CheckEndpoint("Incoming", host, port, security, problems);
        return (protocol, new MailEndpoint(host, port, security));
    }

    private static (OutgoingProtocol? Protocol, MailEndpoint? Endpoint) ResolveOutgoing(
        EmailAccountOptions options, IncomingProtocol incoming, List<string> problems)
    {
        var declared = options.Outgoing;
        if (incoming == IncomingProtocol.Graph)
        {
            if (declared.Protocol == OutgoingProtocol.Smtp)
                problems.Add("an account read through Graph also sends through Graph (Outgoing:Protocol Graph)");
            return (OutgoingProtocol.Graph, null);
        }

        if (declared.Protocol == OutgoingProtocol.Graph)
        {
            problems.Add("Outgoing:Protocol Graph needs Incoming:Protocol Graph");
            return (null, null);
        }

        var host = declared.Host?.Trim() ?? PresetSmtpHost(options.Provider);
        if (string.IsNullOrEmpty(host))
            return (null, null);

        var security = declared.Security
            ?? (options.Provider == EmailProvider.Outlook ? TransportSecurity.StartTls : TransportSecurity.SslOnConnect);
        var port = declared.Port
            ?? (security == TransportSecurity.SslOnConnect ? EmailDefaults.SmtpSslPort : EmailDefaults.SmtpStartTlsPort);
        CheckEndpoint("Outgoing", host, port, security, problems);
        return (OutgoingProtocol.Smtp, new MailEndpoint(host, port, security));
    }

    private static ResolvedAuth? ResolveAuth(
        EmailAccountOptions options, string? address, IncomingProtocol incoming, List<string> problems)
    {
        var declared = options.Auth;
        var method = EffectiveAuthMethod(options);
        var username = string.IsNullOrWhiteSpace(declared.Username) ? address : declared.Username.Trim();

        if (method == EmailAuthMethod.Password)
        {
            if (options.Provider == EmailProvider.Outlook)
                problems.Add("Outlook.com and Microsoft 365 no longer accept passwords for mail clients: use Auth:Method OAuth2 with a ClientId");
            if (string.IsNullOrWhiteSpace(declared.PasswordEnvVar))
                problems.Add("Auth:PasswordEnvVar is required: the NAME of the environment variable holding the password, never the password itself");
            return username is null ? null : new ResolvedAuth
            {
                Method = method,
                Username = username,
                PasswordEnvVar = declared.PasswordEnvVar?.Trim(),
            };
        }

        var oauth = ResolveOAuth(options, incoming, problems);
        return username is null || oauth is null ? null : new ResolvedAuth
        {
            Method = method,
            Username = username,
            OAuth = oauth,
        };
    }

    private static OAuthSettings? ResolveOAuth(EmailAccountOptions options, IncomingProtocol incoming, List<string> problems)
    {
        var declared = options.Auth;
        var clientId = declared.ClientId?.Trim();
        if (string.IsNullOrEmpty(clientId))
        {
            problems.Add("Auth:ClientId is required for OAuth2 (the id of your Google Cloud or Microsoft Entra application)");
            return null;
        }

        var secretEnvVar = string.IsNullOrWhiteSpace(declared.ClientSecretEnvVar) ? null : declared.ClientSecretEnvVar.Trim();
        switch (options.Provider)
        {
            case EmailProvider.Gmail:
                if (secretEnvVar is null)
                    problems.Add("Auth:ClientSecretEnvVar is required for Gmail OAuth2: Google authenticates a desktop client's token requests with its client secret");
                return new OAuthSettings
                {
                    ClientId = clientId,
                    ClientSecretEnvVar = secretEnvVar,
                    Flow = OAuthFlow.LoopbackPkce,
                    TokenEndpoint = EmailDefaults.GoogleTokenEndpoint,
                    AuthorizationEndpoint = EmailDefaults.GoogleAuthorizationEndpoint,
                    Scopes = [EmailDefaults.GoogleMailScope],
                };

            case EmailProvider.Outlook:
                var tenant = string.IsNullOrWhiteSpace(declared.Tenant) ? EmailDefaults.MicrosoftDefaultTenant : declared.Tenant.Trim();
                if (!tenant.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-'))
                {
                    problems.Add($"Auth:Tenant '{tenant}' is not a tenant alias or id");
                    return null;
                }

                var authority = new Uri(EmailDefaults.MicrosoftAuthorityHost, $"{tenant}/oauth2/v2.0/");
                return new OAuthSettings
                {
                    ClientId = clientId,
                    ClientSecretEnvVar = secretEnvVar,
                    Flow = OAuthFlow.DeviceCode,
                    TokenEndpoint = new Uri(authority, "token"),
                    AuthorizationEndpoint = new Uri(authority, "authorize"),
                    DeviceCodeEndpoint = new Uri(authority, "devicecode"),
                    Scopes = MicrosoftScopes(incoming),
                    ScopesOnRefresh = true,
                };

            default:
                problems.Add("OAuth2 is available with the Gmail and Outlook presets; a Custom account signs in with a password");
                return null;
        }
    }

    private static string[] MicrosoftScopes(IncomingProtocol incoming) => incoming switch
    {
        IncomingProtocol.Graph => [EmailDefaults.GraphMailReadWriteScope, EmailDefaults.GraphMailSendScope, EmailDefaults.OfflineAccessScope],
        IncomingProtocol.Pop3 => [EmailDefaults.OutlookPopScope, EmailDefaults.OutlookSmtpScope, EmailDefaults.OfflineAccessScope],
        _ => [EmailDefaults.OutlookImapScope, EmailDefaults.OutlookSmtpScope, EmailDefaults.OfflineAccessScope],
    };

    private static SendPolicy ResolveSend(EmailAccountOptions options, List<string> problems)
    {
        var declared = options.Send;
        var patterns = declared.AllowedRecipients
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Select(pattern => pattern.Trim())
            .ToList();

        foreach (var pattern in patterns.Where(pattern => !Security.RecipientPolicy.IsValidPattern(pattern)))
            problems.Add($"Send:AllowedRecipients entry '{pattern}' is neither an address, '*@domain' nor '*'");

        if (declared.MaxRecipients is <= 0)
            problems.Add("Send:MaxRecipients must be positive");
        if (declared.MaxPerHour is <= 0)
            problems.Add("Send:MaxPerHour must be positive");

        return new SendPolicy(patterns, declared.MaxRecipients, declared.MaxPerHour);
    }

    private static TimeSpan? ResolveTimeout(EmailAccountOptions options, List<string> problems)
    {
        if (options.TimeoutSeconds is not { } seconds)
            return null;

        if (seconds <= 0)
        {
            problems.Add("TimeoutSeconds must be positive");
            return null;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private static void CheckEndpoint(string side, string host, int port, TransportSecurity security, List<string> problems)
    {
        if (port is < 1 or > 65535)
            problems.Add($"{side}:Port {port} is not a TCP port");
        if (security == TransportSecurity.None && !IsLoopbackHost(host))
            problems.Add($"{side}:Security None is only accepted towards a local test server (localhost); '{host}' needs SslOnConnect or StartTls");
    }

    private static string? PresetIncomingHost(EmailProvider provider, IncomingProtocol protocol) => (provider, protocol) switch
    {
        (EmailProvider.Gmail, IncomingProtocol.Imap) => EmailDefaults.GmailImapHost,
        (EmailProvider.Gmail, IncomingProtocol.Pop3) => EmailDefaults.GmailPop3Host,
        (EmailProvider.Outlook, IncomingProtocol.Imap) => EmailDefaults.OutlookImapHost,
        (EmailProvider.Outlook, IncomingProtocol.Pop3) => EmailDefaults.OutlookPop3Host,
        _ => null,
    };

    private static string? PresetSmtpHost(EmailProvider provider) => provider switch
    {
        EmailProvider.Gmail => EmailDefaults.GmailSmtpHost,
        EmailProvider.Outlook => EmailDefaults.OutlookSmtpHost,
        _ => null,
    };

    private static int DefaultPort(IncomingProtocol protocol, TransportSecurity security) => (protocol, security) switch
    {
        (IncomingProtocol.Pop3, TransportSecurity.SslOnConnect) => EmailDefaults.Pop3SslPort,
        (IncomingProtocol.Pop3, _) => EmailDefaults.Pop3StartTlsPort,
        (_, TransportSecurity.SslOnConnect) => EmailDefaults.ImapSslPort,
        _ => EmailDefaults.ImapStartTlsPort,
    };
}
