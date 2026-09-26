namespace Orkeon.Tools.Email.Configuration;

/// <summary>
/// The <c>Orkeon:Tools:Email</c> section, bound as declared. Nothing here is validated at bind
/// time: an account is checked when a tool or a command first uses it, so a broken e-mail
/// section never breaks a crew that sends no mail.
/// </summary>
internal sealed class EmailToolsOptions
{
    /// <summary>The account a call uses when it names none. Optional with a single account.</summary>
    public string? DefaultAccount { get; set; }

    /// <summary>
    /// Physical directory holding the OAuth tokens, for a host whose per-user settings
    /// directory is not the right one (a service account). Read by the host, not by the tools.
    /// </summary>
    public string? CredentialsDirectory { get; set; }

    /// <summary>The accounts, by name. The name is what an agent passes as <c>account</c>.</summary>
    public Dictionary<string, EmailAccountOptions> Accounts { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How received content is screened before an agent reads it.</summary>
    public EmailScreeningOptions Screening { get; } = new();
}

/// <summary>Screening policy for received content.</summary>
internal sealed class EmailScreeningOptions
{
    /// <summary>
    /// When true, a message the prompt-injection detector rejects has its body withheld.
    /// Off by default: the detector was tuned on web pages, and newsletters trip it.
    /// </summary>
    public bool WithholdRejected { get; set; }
}

/// <summary>One account as declared in the settings.</summary>
internal sealed class EmailAccountOptions
{
    /// <summary>The preset: <c>Gmail</c>, <c>Outlook</c> or <c>Custom</c> (the default).</summary>
    public EmailProvider Provider { get; set; }

    /// <summary>The account's address; also the <c>From</c> of every message it sends.</summary>
    public string? Address { get; set; }

    /// <summary>Display name paired with <see cref="Address"/> in <c>From</c>.</summary>
    public string? DisplayName { get; set; }

    /// <summary>What an agent may do, e.g. <c>"Read, Organize, Draft"</c>. Mandatory.</summary>
    public EmailRights Rights { get; set; }

    /// <summary>How mail is read.</summary>
    public EmailIncomingOptions Incoming { get; } = new();

    /// <summary>How mail is sent.</summary>
    public EmailOutgoingOptions Outgoing { get; } = new();

    /// <summary>How the account authenticates.</summary>
    public EmailAuthOptions Auth { get; } = new();

    /// <summary>Guard rails on sending.</summary>
    public EmailSendOptions Send { get; } = new();

    /// <summary>Protocol timeout in seconds. Unset keeps the library default.</summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>
    /// Whether a sent message is appended to the Sent folder. Unset follows the preset: Gmail,
    /// Outlook and Graph file sent mail themselves, a custom server usually does not.
    /// </summary>
    public bool? SaveSentCopy { get; set; }
}

/// <summary>The reading side of an account.</summary>
internal sealed class EmailIncomingOptions
{
    /// <summary><c>Imap</c>, <c>Pop3</c> or <c>Graph</c>; the preset decides when unset.</summary>
    public IncomingProtocol? Protocol { get; set; }

    /// <summary>Server host name.</summary>
    public string? Host { get; set; }

    /// <summary>Server port.</summary>
    public int? Port { get; set; }

    /// <summary>Transport security.</summary>
    public TransportSecurity? Security { get; set; }
}

/// <summary>The sending side of an account.</summary>
internal sealed class EmailOutgoingOptions
{
    /// <summary><c>Smtp</c> or <c>Graph</c>; the preset decides when unset.</summary>
    public OutgoingProtocol? Protocol { get; set; }

    /// <summary>Server host name.</summary>
    public string? Host { get; set; }

    /// <summary>Server port.</summary>
    public int? Port { get; set; }

    /// <summary>Transport security.</summary>
    public TransportSecurity? Security { get; set; }
}

/// <summary>Authentication of an account. Secrets are never written here, only named.</summary>
internal sealed class EmailAuthOptions
{
    /// <summary><c>Password</c> or <c>OAuth2</c>.</summary>
    public EmailAuthMethod? Method { get; set; }

    /// <summary>Login name; defaults to the address.</summary>
    public string? Username { get; set; }

    /// <summary>Name of the environment variable holding the password or app password.</summary>
    public string? PasswordEnvVar { get; set; }

    /// <summary>OAuth client id (Google Cloud or Microsoft Entra application).</summary>
    public string? ClientId { get; set; }

    /// <summary>Name of the environment variable holding the OAuth client secret (Google desktop clients).</summary>
    public string? ClientSecretEnvVar { get; set; }

    /// <summary>Microsoft tenant: <c>consumers</c> (default), <c>organizations</c>, <c>common</c> or a tenant id.</summary>
    public string? Tenant { get; set; }
}

/// <summary>Guard rails on sending.</summary>
internal sealed class EmailSendOptions
{
    /// <summary>
    /// Who may receive mail from this account: an address, <c>*@domain</c>, or <c>*</c> for
    /// anyone. Empty means nobody: sending is closed until an operator opens it.
    /// </summary>
    public List<string> AllowedRecipients { get; } = [];

    /// <summary>Most recipients one message may have. Unset leaves the limit to the server.</summary>
    public int? MaxRecipients { get; set; }

    /// <summary>Most messages this process sends per hour from the account. Unset means no cap.</summary>
    public int? MaxPerHour { get; set; }
}
