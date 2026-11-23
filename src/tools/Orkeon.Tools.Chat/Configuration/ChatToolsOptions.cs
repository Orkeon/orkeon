namespace Orkeon.Tools.Chat.Configuration;

/// <summary>
/// The <c>Orkeon:Tools:Chat</c> section, bound by <see cref="ChatOptionsBinder"/>. Nothing here
/// is validated at bind time, and binding never throws: an account is checked when a tool or a
/// command first uses it, so a broken chat section never breaks a crew that opens no chat.
/// </summary>
internal sealed class ChatToolsOptions
{
    /// <summary>The account a call uses when it names none. Optional with a single account.</summary>
    public string? DefaultAccount { get; set; }

    /// <summary>
    /// Physical directory holding the OAuth tokens, for a host whose per-user settings
    /// directory is not the right one (a service account). Read by the host, not by the tools.
    /// </summary>
    public string? CredentialsDirectory { get; set; }

    /// <summary>The accounts, by name. The name is what an agent passes as <c>account</c>.</summary>
    public Dictionary<string, ChatAccountOptions> Accounts { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How the messages an agent reads are screened.</summary>
    public ChatScreeningOptions Screening { get; } = new();

    /// <summary>Section settings that could not be read; every account reports them until they are fixed.</summary>
    internal List<string> SectionProblems { get; } = [];

    /// <summary>The values each account declares that could not be read, by account name.</summary>
    internal Dictionary<string, List<string>> AccountProblems { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Screening policy for the messages an agent reads.</summary>
internal sealed class ChatScreeningOptions
{
    /// <summary>
    /// When true, a message the prompt-injection detector rejects has its text withheld. Off by
    /// default, as for e-mail: the detector flags; rights and places bound an agent.
    /// </summary>
    public bool WithholdRejected { get; set; }
}

/// <summary>One account as declared in the settings.</summary>
internal sealed class ChatAccountOptions
{
    /// <summary><c>Teams</c>, or <c>TeamsWebhook</c> to post into one place with no sign-in. Mandatory.</summary>
    public ChatProvider? Provider { get; set; }

    /// <summary>What an agent may do, e.g. <c>"Read, Send"</c>. Mandatory.</summary>
    public ChatRights Rights { get; set; }

    /// <summary>How the account authenticates. Not read for a <c>TeamsWebhook</c>.</summary>
    public ChatAuthOptions Auth { get; } = new();

    /// <summary>The webhook of a <c>TeamsWebhook</c> account.</summary>
    public ChatWebhookOptions Webhook { get; } = new();

    /// <summary>The Microsoft cloud: <c>Global</c> (default), <c>UsGov</c> or <c>UsGovDoD</c>. Not read for a <c>TeamsWebhook</c>.</summary>
    public ChatCloud? Cloud { get; set; }

    /// <summary>What an agent may read.</summary>
    public ChatReadOptions Read { get; } = new();

    /// <summary>Guard rails on posting.</summary>
    public ChatSendOptions Send { get; } = new();
}

/// <summary>Authentication of an account. No secret is written here.</summary>
internal sealed class ChatAuthOptions
{
    /// <summary><c>OAuth2</c> (default): a user signs in. <c>ClientCredentials</c>: an application signs in as itself, to read alone.</summary>
    public ChatAuthMethod? Method { get; set; }

    /// <summary>The id of your Microsoft Entra application.</summary>
    public string? ClientId { get; set; }

    /// <summary>The tenant id or domain; <c>organizations</c> when unset. Mandatory for <c>ClientCredentials</c>.</summary>
    public string? Tenant { get; set; }

    /// <summary>
    /// What the tenant administrator has consented to for the application: <c>ChannelMessages</c>,
    /// <c>ChannelEdit</c>, <c>Meetings</c>, <c>ChannelMembers</c>. Each adds its scope to the next sign-in.
    /// </summary>
    public List<string> AdminGranted { get; } = [];

    /// <summary>Name of the environment variable holding the path of a PEM file with the certificate and its key, for <c>ClientCredentials</c>.</summary>
    public string? CertificatePathEnvVar { get; set; }

    /// <summary>Name of the environment variable holding the path of a federated token file, for <c>ClientCredentials</c>.</summary>
    public string? FederatedTokenFileEnvVar { get; set; }

    /// <summary>Name of the environment variable holding the client secret, for <c>ClientCredentials</c>.</summary>
    public string? ClientSecretEnvVar { get; set; }
}

/// <summary>The webhook of an account. Its address is a secret, and is never written here.</summary>
internal sealed class ChatWebhookOptions
{
    /// <summary>Name of the environment variable holding the address of the Workflows webhook, never the address itself.</summary>
    public string? UrlEnvVar { get; set; }
}

/// <summary>What an agent may read.</summary>
internal sealed class ChatReadOptions
{
    /// <summary>The places an agent may read. Empty means none.</summary>
    public List<string> AllowedPlaces { get; } = [];
}

/// <summary>Guard rails on posting.</summary>
internal sealed class ChatSendOptions
{
    /// <summary>The places an agent may post in. Empty means none.</summary>
    public List<string> AllowedPlaces { get; } = [];

    /// <summary>The most posts the account attempts in an hour, in this process. Unset sets no cap.</summary>
    public int? MaxPerHour { get; set; }
}
