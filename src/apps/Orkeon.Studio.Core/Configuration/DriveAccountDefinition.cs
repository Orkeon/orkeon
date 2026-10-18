using System.Collections.Immutable;

namespace Orkeon.Studio.Core.Configuration;

/// <summary>
/// One drive account as <c>Orkeon:Tools:Drive:Accounts</c> spells it: every key the engine's
/// binder reads, flattened, on the model of <see cref="EmailAccountDefinition"/>. A field left
/// <see langword="null"/> is a key that is absent — never an empty string, which the engine reads
/// as a value and not as "unset" — so the engine's own default applies to it. The provider is kept
/// as the text the file holds, in the spelling of <see cref="DriveSection"/>; this type validates nothing.
/// </summary>
public sealed record DriveAccountDefinition
{
    /// <summary>The dictionary key under <c>Orkeon:Tools:Drive:Accounts</c>; what an agent passes as <c>account</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The drive, one of <see cref="DriveSection.Providers"/>; mandatory to the engine.</summary>
    public string? Provider { get; init; }

    /// <summary>What an agent may do. <see cref="DriveRights.None"/> writes no key.</summary>
    public DriveRights Rights { get; init; }

    /// <summary>
    /// The <c>Rights</c> text the file holds when it is not a list of rights the engine reads
    /// (a misspelt name, an undefined number, <c>None</c>), set only while <see cref="Rights"/> is
    /// <see cref="DriveRights.None"/>: it is written back as it was until a right is chosen.
    /// </summary>
    public string? RightsRaw { get; init; }

    /// <summary><c>Root</c>: the folder the agent is confined to, as a path from the root of the drive; absent opens the whole drive.</summary>
    public string? Root { get; init; }

    /// <summary><c>Auth:ClientId</c>: the OAuth client id (Google Cloud or Microsoft Entra application).</summary>
    public string? ClientId { get; init; }

    /// <summary><c>Auth:ClientSecretEnvVar</c>: the <i>name</i> of the variable that holds the OAuth client secret.</summary>
    public string? ClientSecretEnvVar { get; init; }

    /// <summary><c>Auth:Tenant</c>: a Microsoft tenant alias or identifier; OneDrive only.</summary>
    public string? Tenant { get; init; }

    /// <summary><c>Share:AllowedRecipients</c>: an address, <c>*@domain</c> or <c>*</c>; empty means nobody.</summary>
    public ImmutableList<string> AllowedRecipients { get; init; } = [];

    /// <summary><c>Share:AllowPublicLinks</c>: whether an agent may create a link anyone holding it can open; absent means no.</summary>
    public bool? AllowPublicLinks { get; init; }

    /// <summary><c>MaxUploadMegabytes</c>: the largest file an agent may upload; absent means the engine's 100.</summary>
    public int? MaxUploadMegabytes { get; init; }

    /// <summary><c>TimeoutSeconds</c>: the HTTP timeout; absent keeps the client's own.</summary>
    public int? TimeoutSeconds { get; init; }
}
