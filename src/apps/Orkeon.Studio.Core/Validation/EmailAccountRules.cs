using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Orkeon.Studio.Core.Configuration;
using static Orkeon.Studio.Core.Configuration.EmailSection.Values;

namespace Orkeon.Studio.Core.Validation;

/// <summary>
/// The rules the engine applies to an e-mail account (STUDIO-66), spelt again in Core: what
/// <c>orkeon run</c> will say of the account when a tool or <c>orkeon email</c> names it. A broken
/// account stops no run — the engine sets it aside, with its problems —, so every finding is a
/// warning, in the engine's own sentence, at the key that fixes it.
/// </summary>
/// <remarks>
/// Core does not reference <c>Orkeon.Tools.Email</c> (a form has no use for MailKit and MimeKit):
/// the sentences, the provider presets and the order of the checks are copies of
/// <c>EmailOptionsBinder</c> and <c>EmailAccountResolver</c>, and <c>EmailValidationOracleTests</c>
/// holds each of them to the real binder and the real resolver. The address rule is the one
/// exception to "the same": the engine parses an address with MimeKit, Studio only asks for one
/// <c>@</c> with something on each side and no blank.
/// </remarks>
public static class EmailAccountRules
{
    /// <summary>The longest account name the engine accepts.</summary>
    private const int MaxNameLength = 64;

    /// <summary>The tenant of an Outlook account that names none: personal Microsoft accounts.</summary>
    internal const string DefaultTenant = "consumers";

    // The hosts and the conventional ports of the presets, as the engine's EmailDefaults holds them.
    private const string GmailImapHost = "imap.gmail.com";
    private const string GmailPop3Host = "pop.gmail.com";
    private const string GmailSmtpHost = "smtp.gmail.com";
    private const string OutlookImapHost = "outlook.office365.com";
    private const string OutlookPop3Host = "outlook.office365.com";
    private const string OutlookSmtpHost = "smtp-mail.outlook.com";
    private const int ImapSslPort = 993;
    private const int ImapStartTlsPort = 143;
    private const int Pop3SslPort = 995;
    private const int Pop3StartTlsPort = 110;
    private const int SmtpSslPort = 465;
    private const int SmtpStartTlsPort = 587;

    private const string IncomingProtocolKey = EmailSection.Keys.Incoming + ":" + EmailSection.Keys.Protocol;
    private const string IncomingHostKey = EmailSection.Keys.Incoming + ":" + EmailSection.Keys.Host;
    private const string IncomingPortKey = EmailSection.Keys.Incoming + ":" + EmailSection.Keys.Port;
    private const string IncomingSecurityKey = EmailSection.Keys.Incoming + ":" + EmailSection.Keys.Security;
    private const string OutgoingProtocolKey = EmailSection.Keys.Outgoing + ":" + EmailSection.Keys.Protocol;
    private const string OutgoingHostKey = EmailSection.Keys.Outgoing + ":" + EmailSection.Keys.Host;
    private const string OutgoingPortKey = EmailSection.Keys.Outgoing + ":" + EmailSection.Keys.Port;
    private const string OutgoingSecurityKey = EmailSection.Keys.Outgoing + ":" + EmailSection.Keys.Security;
    private const string AuthMethodKey = EmailSection.Keys.Auth + ":" + EmailSection.Keys.Method;
    private const string PasswordEnvVarKey = EmailSection.Keys.Auth + ":" + EmailSection.Keys.PasswordEnvVar;
    private const string ClientIdKey = EmailSection.Keys.Auth + ":" + EmailSection.Keys.ClientId;
    private const string ClientSecretEnvVarKey = EmailSection.Keys.Auth + ":" + EmailSection.Keys.ClientSecretEnvVar;
    private const string TenantKey = EmailSection.Keys.Auth + ":" + EmailSection.Keys.Tenant;
    private const string AllowedRecipientsKey = EmailSection.Keys.Send + ":" + EmailSection.Keys.AllowedRecipients;
    private const string MaxRecipientsKey = EmailSection.Keys.Send + ":" + EmailSection.Keys.MaxRecipients;
    private const string MaxPerHourKey = EmailSection.Keys.Send + ":" + EmailSection.Keys.MaxPerHour;
    private const string WithholdRejectedPath =
        EmailSection.SectionPath + ":" + EmailSection.Keys.Screening + ":" + EmailSection.Keys.WithholdRejected;

    /// <summary>How the binder reads a setting that is not a string.</summary>
    private enum Kind
    {
        Provider,
        Rights,
        IncomingProtocol,
        OutgoingProtocol,
        Security,
        AuthMethod,
        Integer,
        Boolean,
    }

    /// <summary>
    /// Every account setting the binder converts, in the order it checks them. An optional one may
    /// be empty, which reads as absent; <c>Provider</c> and <c>Rights</c> may not.
    /// </summary>
    private static readonly (string Key, Kind Kind, bool Optional)[] TypedSettings =
    [
        (EmailSection.Keys.Provider, Kind.Provider, false),
        (EmailSection.Keys.Rights, Kind.Rights, false),
        (IncomingProtocolKey, Kind.IncomingProtocol, true),
        (IncomingPortKey, Kind.Integer, true),
        (IncomingSecurityKey, Kind.Security, true),
        (OutgoingProtocolKey, Kind.OutgoingProtocol, true),
        (OutgoingPortKey, Kind.Integer, true),
        (OutgoingSecurityKey, Kind.Security, true),
        (AuthMethodKey, Kind.AuthMethod, true),
        (MaxRecipientsKey, Kind.Integer, true),
        (MaxPerHourKey, Kind.Integer, true),
        (EmailSection.Keys.TimeoutSeconds, Kind.Integer, true),
        (EmailSection.Keys.SaveSentCopy, Kind.Boolean, true),
    ];

    /// <summary>The keys an account carries, in the order the engine lists them.</summary>
    internal static readonly string[] AccountKeys =
    [
        EmailSection.Keys.Provider, EmailSection.Keys.Address, EmailSection.Keys.DisplayName, EmailSection.Keys.Rights,
        EmailSection.Keys.Incoming, EmailSection.Keys.Outgoing, EmailSection.Keys.Auth, EmailSection.Keys.Send,
        EmailSection.Keys.TimeoutSeconds, EmailSection.Keys.SaveSentCopy,
    ];

    private static readonly string[] EndpointKeys =
        [EmailSection.Keys.Protocol, EmailSection.Keys.Host, EmailSection.Keys.Port, EmailSection.Keys.Security];

    /// <summary>The keys each object of an account carries, by the object's key.</summary>
    internal static readonly Dictionary<string, string[]> ObjectKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        [EmailSection.Keys.Incoming] = EndpointKeys,
        [EmailSection.Keys.Outgoing] = EndpointKeys,
        [EmailSection.Keys.Auth] =
        [
            EmailSection.Keys.Method, EmailSection.Keys.Username, EmailSection.Keys.PasswordEnvVar,
            EmailSection.Keys.ClientId, EmailSection.Keys.ClientSecretEnvVar, EmailSection.Keys.Tenant,
        ],
        [EmailSection.Keys.Send] = [EmailSection.Keys.AllowedRecipients, EmailSection.Keys.MaxRecipients, EmailSection.Keys.MaxPerHour],
    };

    /// <summary>
    /// The findings of an account as <paramref name="definition"/> holds it, under
    /// <paramref name="name"/>: empty when the engine will use the account. A value the engine
    /// cannot read (a provider it does not know, rights that are no list of rights) is all it
    /// says of the account, as the engine then reads nothing else of it. Each field is judged as
    /// the engine reads it: an empty string is a value, not an absent key — an empty
    /// <c>Incoming:Host</c> blocks the preset's.
    /// </summary>
    /// <remarks>
    /// One finding is Studio's own and not the engine's: a <c>PasswordEnvVar</c> or a
    /// <c>ClientSecretEnvVar</c> that cannot be the name of a variable — the secret pasted in the
    /// field that names it. The engine resolves such an account, and fails when it reads the secret.
    /// </remarks>
    public static IReadOnlyList<ValidationMessage> Check(string name, EmailAccountDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(definition);

        var findings = new Findings(name);
        foreach (var (key, kind, optional) in TypedSettings)
        {
            if (Unreadable(key, kind, optional, WrittenText(definition, key)) is { } problem)
                findings.Add(ValidationCodes.EmailValue, key, problem);
        }

        if (findings.Messages.Count == 0)
            Resolve(name, definition, findings);

        return findings.Messages;
    }

    /// <summary>
    /// The findings of the account <paramref name="name"/> (its exact spelling) as the file holds
    /// it, which is what the run reads: a key no account carries and a value the binder cannot
    /// convert first — with the screening switch of the section when it is unreadable, which sets
    /// every account aside —, and the engine then says nothing else of the account; otherwise the
    /// rules of <see cref="Check(string, EmailAccountDefinition)"/>. Keys equal but for the case
    /// (<see cref="EmailTwinKeys"/>) come before all of it and alone: the run reads none of them on
    /// its own, when it reads the file at all. Empty when the file holds no such account.
    /// </summary>
    public static IReadOnlyList<ValidationMessage> Check(AppSettingsDocument document, string name)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(name);

        if (document.Email.AccountsNode is not { } accounts || !accounts.TryGetPropertyValue(name, out var node))
            return [];

        // What stands under the name may be no object at all: the engine then reads an account
        // that declares nothing.
        var account = node as JsonObject;
        if (account is not null && EmailTwinKeys.Under(account, $"{EmailSection.AccountsPath}:{name}").ToList() is { Count: > 0 } twins)
            return twins;

        var findings = new Findings(name);
        if (ScreeningProblem(document) is { } screening)
            findings.Add(ValidationCodes.EmailScreening, string.Empty, screening);

        if (account is not null)
        {
            UnknownKeys(account, AccountKeys, prefix: string.Empty, findings);
            foreach (var (key, kind, optional) in TypedSettings)
            {
                if (Unreadable(key, kind, optional, ConfigurationText(Child(account, key))) is { } problem)
                    findings.Add(ValidationCodes.EmailValue, key, problem);
            }
        }

        return findings.Messages.Count > 0 ? findings.Messages : Check(name, EmailSection.ReadAccount(name, account));
    }

    /// <summary>
    /// Whether <paramref name="name"/> is a name the engine accepts for an account (and for its
    /// token file): what a form checks before it writes an account under it (STUDIO-67).
    /// </summary>
    public static bool IsValidName(string? name) =>
        !string.IsNullOrEmpty(name)
        && name.Length <= MaxNameLength
        && char.IsAsciiLetterOrDigit(name[0])
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    /// <summary>
    /// Whether <paramref name="host"/> designates this machine, the only place the engine accepts
    /// <c>Security: None</c> towards: <c>localhost</c> or a loopback address, bracketed or not.
    /// </summary>
    private static bool IsLoopbackHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));

    /// <summary>
    /// Whether <paramref name="pattern"/> is an entry <c>Send:AllowedRecipients</c> understands: an
    /// address alone, <c>*@domain</c>, or <c>*</c> for anyone. The engine trims an entry before it
    /// reads it, and so does a caller.
    /// </summary>
    public static bool IsRecipientPattern(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        if (pattern == "*")
            return true;

        if (pattern.StartsWith("*@", StringComparison.Ordinal))
        {
            var domain = pattern[2..];
            return domain.Length > 0 && !domain.Contains('@', StringComparison.Ordinal) && !domain.Contains('*', StringComparison.Ordinal);
        }

        return !pattern.Contains('*', StringComparison.Ordinal) && IsBareAddress(pattern);
    }

    /// <summary>
    /// The address the engine keeps of an <c>Address</c> value — what stands between the angle
    /// brackets of <c>Name &lt;me@example.com&gt;</c>, else the text itself, trimmed —, or null when
    /// it is none: one <c>@</c> with something on each side, and no blank.
    /// </summary>
    internal static string? MailboxOf(string? address)
    {
        var text = address?.Trim();
        if (string.IsNullOrEmpty(text))
            return null;

        var open = text.LastIndexOf('<');
        if (open >= 0 && text.EndsWith('>'))
            text = text[(open + 1)..^1].Trim();

        return IsBareAddress(text) ? text : null;
    }

    /// <summary>
    /// The member of <paramref name="names"/> the engine binds <paramref name="text"/> to, in the
    /// spelling of <see cref="EmailSection"/>, or null when it binds none: a name without regard to
    /// case, or the member's number, as the configuration binder converts an enumeration.
    /// </summary>
    internal static string? Member(IReadOnlyList<string> names, string? text) =>
        text is not null && EnumerationValue(names, text, flags: false) is { } value && value >= 0 && value < names.Count
            ? names[(int)value]
            : null;

    /// <summary>The host the preset of <paramref name="provider"/> reads mail from, or null for <c>Custom</c>.</summary>
    internal static string? PresetIncomingHost(string provider, string protocol) => (provider, protocol) switch
    {
        (Gmail, Imap) => GmailImapHost,
        (Gmail, Pop3) => GmailPop3Host,
        (Outlook, Imap) => OutlookImapHost,
        (Outlook, Pop3) => OutlookPop3Host,
        _ => null,
    };

    /// <summary>The SMTP host the preset of <paramref name="provider"/> sends through, or null for <c>Custom</c>.</summary>
    internal static string? PresetSmtpHost(string provider) => provider switch
    {
        Gmail => GmailSmtpHost,
        Outlook => OutlookSmtpHost,
        _ => null,
    };

    /// <summary>The conventional port of an IMAP or POP3 server under <paramref name="security"/>.</summary>
    internal static int DefaultIncomingPort(string protocol, string security) => (protocol, security) switch
    {
        (Pop3, SslOnConnect) => Pop3SslPort,
        (Pop3, _) => Pop3StartTlsPort,
        (_, SslOnConnect) => ImapSslPort,
        _ => ImapStartTlsPort,
    };

    /// <summary>The conventional SMTP submission port under <paramref name="security"/>.</summary>
    internal static int DefaultSmtpPort(string security) => security == SslOnConnect ? SmtpSslPort : SmtpStartTlsPort;

    /// <summary>
    /// The engine's sentence on <c>Screening:WithholdRejected</c> when it is neither true nor
    /// false — every account is then set aside on it —, or null.
    /// </summary>
    internal static string? ScreeningProblem(AppSettingsDocument document)
    {
        var text = ConfigurationText(document.Email.WithholdRejectedNode);
        return string.IsNullOrEmpty(text) || bool.TryParse(text, out _)
            ? null
            : $"{WithholdRejectedPath} '{text}' is neither true nor false";
    }

    /// <summary>The engine's resolver, rule by rule and in its order.</summary>
    private static void Resolve(string name, EmailAccountDefinition definition, Findings findings)
    {
        if (!IsValidName(name))
        {
            findings.Add(ValidationCodes.EmailName, string.Empty,
                "the account name may only hold letters, digits, '.', '_' and '-', starts with a letter or a digit, and has 64 characters at most");
        }

        var address = definition.Address?.Trim();
        if (string.IsNullOrEmpty(address))
            findings.Add(ValidationCodes.EmailAddress, EmailSection.Keys.Address, "Address is required");
        else if (MailboxOf(address) is null)
            findings.Add(ValidationCodes.EmailAddress, EmailSection.Keys.Address, $"Address '{address}' is not an e-mail address");

        var rights = definition.Rights & EmailSection.AllRights;
        if (rights == EmailRights.None)
        {
            findings.Add(ValidationCodes.EmailRights, EmailSection.Keys.Rights,
                "Rights is required and says what an agent may do, e.g. \"Rights\": \"Read, Organize, Draft\"");
        }

        var provider = Member(EmailSection.Providers, definition.Provider) ?? Custom;
        var effective = EmailAccountEffective.Of(definition);

        ResolveIncoming(provider, effective, findings);
        ResolveOutgoing(definition, effective, findings);
        ResolveAuth(definition, provider, effective, findings);
        ResolveSend(definition, findings);

        if (definition.TimeoutSeconds is <= 0)
            findings.Add(ValidationCodes.EmailServer, EmailSection.Keys.TimeoutSeconds, "TimeoutSeconds must be positive");

        if (rights.HasFlag(EmailRights.Send) && !effective.CanSend)
        {
            findings.Add(ValidationCodes.EmailSend, OutgoingHostKey,
                "Rights grant Send but the account declares no outgoing server (Outgoing:Host)");
        }
    }

    private static void ResolveIncoming(string provider, EmailAccountEffective effective, Findings findings)
    {
        if (effective.IncomingProtocol == Graph)
        {
            if (provider != Outlook)
                findings.Add(ValidationCodes.EmailServer, IncomingProtocolKey, "Incoming:Protocol Graph is only available with the Outlook preset");
        }
        else if (effective.IncomingHost is not { } host)
        {
            findings.Add(ValidationCodes.EmailServer, IncomingHostKey, "Incoming:Host is required for a Custom account");
        }
        else
        {
            CheckEndpoint(EmailSection.Keys.Incoming, host, effective.IncomingPort, effective.IncomingSecurity, findings);
        }
    }

    private static void ResolveOutgoing(EmailAccountDefinition definition, EmailAccountEffective effective, Findings findings)
    {
        var declared = Member(EmailSection.OutgoingProtocols, definition.OutgoingProtocol);
        if (effective.IncomingProtocol == Graph)
        {
            if (declared == Smtp)
            {
                findings.Add(ValidationCodes.EmailServer, OutgoingProtocolKey,
                    "an account read through Graph also sends through Graph (Outgoing:Protocol Graph)");
            }
        }
        else if (declared == Graph)
        {
            findings.Add(ValidationCodes.EmailServer, OutgoingProtocolKey, "Outgoing:Protocol Graph needs Incoming:Protocol Graph");
        }
        else if (effective.OutgoingHost is { } host)
        {
            CheckEndpoint(EmailSection.Keys.Outgoing, host, effective.OutgoingPort, effective.OutgoingSecurity, findings);
        }
    }

    private static void CheckEndpoint(string side, string host, int? port, string? security, Findings findings)
    {
        if (port is < 1 or > 65535)
        {
            findings.Add(ValidationCodes.EmailServer, $"{side}:{EmailSection.Keys.Port}",
                string.Create(CultureInfo.InvariantCulture, $"{side}:Port {port} is not a TCP port"));
        }

        if (security == NoSecurity && !IsLoopbackHost(host))
        {
            findings.Add(ValidationCodes.EmailServer, $"{side}:{EmailSection.Keys.Security}",
                $"{side}:Security None is only accepted towards a local test server (localhost); '{host}' needs SslOnConnect or StartTls");
        }
    }

    private static void ResolveAuth(EmailAccountDefinition definition, string provider, EmailAccountEffective effective, Findings findings)
    {
        if (effective.AuthMethod == Password)
            ResolvePassword(definition, provider, findings);
        else
            ResolveOAuth2(definition, provider, effective, findings);

        // Studio's own, and never the value: it may be the secret, pasted in the field that names it.
        foreach (var (key, variable) in new[] { (PasswordEnvVarKey, definition.PasswordEnvVar), (ClientSecretEnvVarKey, definition.ClientSecretEnvVar) })
        {
            if (!LlmSection.IsVariableName(variable))
            {
                findings.Add(ValidationCodes.EmailAuth, key,
                    $"{key} must be the name of an environment variable (no '=', space or line break), never the secret itself");
            }
        }
    }

    private static void ResolvePassword(EmailAccountDefinition definition, string provider, Findings findings)
    {
        if (provider == Outlook)
        {
            findings.Add(ValidationCodes.EmailAuth, AuthMethodKey,
                "Outlook.com and Microsoft 365 no longer accept passwords for mail clients: use Auth:Method OAuth2 with a ClientId");
        }

        if (string.IsNullOrWhiteSpace(definition.PasswordEnvVar))
        {
            findings.Add(ValidationCodes.EmailAuth, PasswordEnvVarKey,
                "Auth:PasswordEnvVar is required: the NAME of the environment variable holding the password, never the password itself");
        }
    }

    private static void ResolveOAuth2(EmailAccountDefinition definition, string provider, EmailAccountEffective effective, Findings findings)
    {
        if (string.IsNullOrWhiteSpace(definition.ClientId))
        {
            findings.Add(ValidationCodes.EmailAuth, ClientIdKey,
                "Auth:ClientId is required for OAuth2 (the id of your Google Cloud or Microsoft Entra application)");
        }
        else if (provider == Gmail)
        {
            if (string.IsNullOrWhiteSpace(definition.ClientSecretEnvVar))
            {
                findings.Add(ValidationCodes.EmailAuth, ClientSecretEnvVarKey,
                    "Auth:ClientSecretEnvVar is required for Gmail OAuth2: Google authenticates a desktop client's token requests with its client secret");
            }
        }
        else if (provider == Outlook)
        {
            var tenant = effective.Tenant ?? DefaultTenant;
            if (!tenant.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-'))
                findings.Add(ValidationCodes.EmailAuth, TenantKey, $"Auth:Tenant '{tenant}' is not a tenant alias or id");
        }
        else
        {
            findings.Add(ValidationCodes.EmailAuth, AuthMethodKey,
                "OAuth2 is available with the Gmail and Outlook presets; a Custom account signs in with a password");
        }
    }

    private static void ResolveSend(EmailAccountDefinition definition, Findings findings)
    {
        foreach (var pattern in definition.AllowedRecipients
                     .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
                     .Select(pattern => pattern.Trim())
                     .Where(pattern => !IsRecipientPattern(pattern)))
        {
            findings.Add(ValidationCodes.EmailSend, AllowedRecipientsKey,
                $"Send:AllowedRecipients entry '{pattern}' is neither an address, '*@domain' nor '*'");
        }

        if (definition.MaxRecipients is <= 0)
            findings.Add(ValidationCodes.EmailSend, MaxRecipientsKey, "Send:MaxRecipients must be positive");
        if (definition.MaxPerHour is <= 0)
            findings.Add(ValidationCodes.EmailSend, MaxPerHourKey, "Send:MaxPerHour must be positive");
    }

    /// <summary>
    /// The keys of <paramref name="section"/> the engine knows no setting for, at any depth, in the
    /// order the configuration lists them (by name).
    /// </summary>
    private static void UnknownKeys(JsonObject section, string[] known, string prefix, Findings findings)
    {
        foreach (var (key, value) in section.OrderBy(property => property.Key, StringComparer.OrdinalIgnoreCase))
        {
            var path = prefix + key;
            if (!known.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                var owner = prefix.Length == 0 ? "an account" : prefix.TrimEnd(':');
                findings.Add(ValidationCodes.EmailKey, path, $"{path} is not an account setting: {owner} carries {string.Join(", ", known)}");
            }
            else if (value is JsonObject child && ObjectKeys.TryGetValue(key, out var childKeys))
            {
                UnknownKeys(child, childKeys, path + ":", findings);
            }
        }
    }

    /// <summary>
    /// The binder's sentence on a typed setting it cannot convert, or null when it reads
    /// <paramref name="text"/> — or when the key is absent, or optional and empty.
    /// </summary>
    private static string? Unreadable(string key, Kind kind, bool optional, string? text)
    {
        if (text is null || (optional && text.Length == 0))
            return null;

        return kind switch
        {
            Kind.Integer => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ? null : $"{key} '{text}' is not a whole number",
            Kind.Boolean => bool.TryParse(text, out _) ? null : $"{key} '{text}' is neither true nor false",
            Kind.Rights => EnumerationValue(EmailSection.RightNames, text, flags: true) is { } value && (value & ~(long)EmailSection.AllRights) == 0
                ? null
                : $"{key} '{text}' is not a list of {string.Join(", ", EmailSection.RightNames)}, separated by commas",
            _ => Member(NamesOf(kind), text) is not null ? null : $"{key} '{text}' is not one of {string.Join(", ", NamesOf(kind))}",
        };
    }

    private static IReadOnlyList<string> NamesOf(Kind kind) => kind switch
    {
        Kind.Provider => EmailSection.Providers,
        Kind.IncomingProtocol => EmailSection.IncomingProtocols,
        Kind.OutgoingProtocol => EmailSection.OutgoingProtocols,
        Kind.Security => EmailSection.Securities,
        _ => EmailSection.AuthMethods,
    };

    /// <summary>
    /// The value the binder's converter gives <paramref name="text"/> for an enumeration whose
    /// members are <paramref name="names"/> — numbered from 0, or one bit each for
    /// <paramref name="flags"/> with <c>None</c> for 0 —, or null when it converts none. Members are
    /// separated by commas and combined; each is a name, without regard to case, or a number.
    /// </summary>
    private static long? EnumerationValue(IReadOnlyList<string> names, string text, bool flags)
    {
        long value = 0;
        foreach (var part in text.Split(','))
        {
            var token = part.Trim();
            if (token.Length > 0
                && (char.IsAsciiDigit(token[0]) || token[0] is '-' or '+')
                && int.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
            {
                value |= (long)number;
                continue;
            }

            var index = names.Select((name, position) => (name, position))
                .Where(entry => string.Equals(entry.name, token, StringComparison.OrdinalIgnoreCase))
                .Select(entry => (int?)entry.position)
                .FirstOrDefault();
            if (index is { } found)
                value |= flags ? 1L << found : found;
            else if (!(flags && string.Equals(token, nameof(EmailRights.None), StringComparison.OrdinalIgnoreCase)))
                return null;
        }

        return value;
    }

    /// <summary>The text a typed setting holds in <paramref name="definition"/>; a number or a switch always reads.</summary>
    private static string? WrittenText(EmailAccountDefinition definition, string key) => key switch
    {
        EmailSection.Keys.Provider => definition.Provider,
        EmailSection.Keys.Rights => (definition.Rights & EmailSection.AllRights) == EmailRights.None ? definition.RightsRaw : null,
        IncomingProtocolKey => definition.IncomingProtocol,
        IncomingSecurityKey => definition.IncomingSecurity,
        OutgoingProtocolKey => definition.OutgoingProtocol,
        OutgoingSecurityKey => definition.OutgoingSecurity,
        AuthMethodKey => definition.AuthMethod,
        _ => null,
    };

    /// <summary>
    /// The text the JSON configuration provider gives the engine for <paramref name="node"/>: a
    /// string as it is, a number as written, a switch as <c>True</c> or <c>False</c>, an empty list
    /// as an empty string; null for what carries no value of its own.
    /// </summary>
    private static string? ConfigurationText(JsonNode? node) => node switch
    {
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        JsonValue value when value.TryGetValue<bool>(out var flag) => flag ? bool.TrueString : bool.FalseString,
        JsonValue value => value.ToJsonString(),
        JsonArray { Count: 0 } => string.Empty,
        _ => null,
    };

    /// <summary>The node at <paramref name="key"/> under <paramref name="account"/>, keys compared without case as the configuration binds them.</summary>
    private static JsonNode? Child(JsonObject account, string key)
    {
        JsonNode? current = account;
        foreach (var segment in key.Split(':'))
        {
            if (current is not JsonObject container)
                return null;

            current = container.FirstOrDefault(property => string.Equals(property.Key, segment, StringComparison.OrdinalIgnoreCase)).Value;
        }

        return current;
    }

    private static bool IsBareAddress(string text)
    {
        var at = text.IndexOf('@', StringComparison.Ordinal);
        return at > 0
            && at < text.Length - 1
            && text.IndexOf('@', at + 1) < 0
            && !text.Any(c => char.IsWhiteSpace(c) || c is '<' or '>');
    }

    /// <summary>The findings of one account, each a warning at a key under the account's path.</summary>
    private sealed class Findings
    {
        private readonly string _path;

        public Findings(string name)
        {
            _path = $"{EmailSection.AccountsPath}:{name}";
            Messages = [];
        }

        public List<ValidationMessage> Messages { get; }

        public void Add(string code, string key, string text) =>
            Messages.Add(ValidationMessage.Warning(code, text, key.Length == 0 ? _path : $"{_path}:{key}"));
    }
}
