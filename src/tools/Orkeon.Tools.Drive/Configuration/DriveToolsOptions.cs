namespace Orkeon.Tools.Drive.Configuration;

/// <summary>
/// The <c>Orkeon:Tools:Drive</c> section, bound by <see cref="DriveOptionsBinder"/>. Nothing here
/// is validated at bind time, and binding never throws: an account is checked when a tool or a
/// command first uses it, so a broken drive section never breaks a crew that opens no drive.
/// </summary>
internal sealed class DriveToolsOptions
{
    /// <summary>The account a call uses when it names none. Optional with a single account.</summary>
    public string? DefaultAccount { get; set; }

    /// <summary>
    /// Physical directory holding the OAuth tokens, for a host whose per-user settings
    /// directory is not the right one (a service account). Read by the host, not by the tools.
    /// </summary>
    public string? CredentialsDirectory { get; set; }

    /// <summary>The accounts, by name. The name is what an agent passes as <c>account</c>.</summary>
    public Dictionary<string, DriveAccountOptions> Accounts { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How the content an agent reads is screened.</summary>
    public DriveScreeningOptions Screening { get; } = new();

    /// <summary>Section settings that could not be read; every account reports them until they are fixed.</summary>
    internal List<string> SectionProblems { get; } = [];

    /// <summary>The values each account declares that could not be read, by account name.</summary>
    internal Dictionary<string, List<string>> AccountProblems { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Screening policy for the content an agent reads.</summary>
internal sealed class DriveScreeningOptions
{
    /// <summary>
    /// When true, a file the prompt-injection detector rejects has its text withheld. Off by
    /// default, as for e-mail: the detector flags; rights and the root folder bound an agent.
    /// </summary>
    public bool WithholdRejected { get; set; }
}

/// <summary>One account as declared in the settings.</summary>
internal sealed class DriveAccountOptions
{
    /// <summary><c>GoogleDrive</c> or <c>OneDrive</c>. Mandatory.</summary>
    public DriveProvider? Provider { get; set; }

    /// <summary>What an agent may do, e.g. <c>"Read, Write, Organize"</c>. Mandatory.</summary>
    public DriveRights Rights { get; set; }

    /// <summary>
    /// The folder the agent is confined to, as a path from the root of the drive
    /// (<c>Orkeon/agents</c>). Unset opens the whole drive.
    /// </summary>
    public string? Root { get; set; }

    /// <summary>How the account authenticates.</summary>
    public DriveAuthOptions Auth { get; } = new();

    /// <summary>Guard rails on sharing.</summary>
    public DriveShareOptions Share { get; } = new();

    /// <summary>The largest file an agent may upload, in megabytes. Unset means 100.</summary>
    public int? MaxUploadMegabytes { get; set; }

    /// <summary>HTTP timeout in seconds. Unset keeps the client default.</summary>
    public int? TimeoutSeconds { get; set; }
}

/// <summary>Authentication of an account: OAuth2 always. Secrets are never written here, only named.</summary>
internal sealed class DriveAuthOptions
{
    /// <summary>OAuth client id (Google Cloud or Microsoft Entra application).</summary>
    public string? ClientId { get; set; }

    /// <summary>Name of the environment variable holding the OAuth client secret (Google desktop clients).</summary>
    public string? ClientSecretEnvVar { get; set; }

    /// <summary>Microsoft tenant: <c>consumers</c> (default), <c>organizations</c>, <c>common</c> or a tenant id.</summary>
    public string? Tenant { get; set; }
}

/// <summary>Guard rails on sharing.</summary>
internal sealed class DriveShareOptions
{
    /// <summary>
    /// Who an agent may share with: an address, <c>*@domain</c>, or <c>*</c> for anyone with an
    /// address. Empty means nobody: sharing is closed until an operator opens it.
    /// </summary>
    public List<string> AllowedRecipients { get; } = [];

    /// <summary>Whether an agent may create a link anyone holding it can open. Off by default.</summary>
    public bool? AllowPublicLinks { get; set; }
}
