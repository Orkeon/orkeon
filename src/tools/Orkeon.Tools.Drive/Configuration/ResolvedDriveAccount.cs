using Orkeon.Tools.Abstractions.Auth;

namespace Orkeon.Tools.Drive.Configuration;

/// <summary>Guard rails applied to every share the account makes.</summary>
/// <param name="AllowedRecipients">Allowed recipient patterns; empty allows nobody.</param>
/// <param name="AllowPublicLinks">Whether links anyone can open are allowed.</param>
internal sealed record SharePolicy(IReadOnlyList<string> AllowedRecipients, bool AllowPublicLinks);

/// <summary>
/// An account after presets and validation: everything an engine needs, nothing left to guess.
/// </summary>
internal sealed record ResolvedDriveAccount
{
    /// <summary>The name agents address the account by.</summary>
    public required string Name { get; init; }

    /// <summary>The drive it opens.</summary>
    public required DriveProvider Provider { get; init; }

    /// <summary>What an agent may do.</summary>
    public required DriveRights Rights { get; init; }

    /// <summary>The root folder's path segments from the root of the drive; empty for the whole drive.</summary>
    public required IReadOnlyList<string> Root { get; init; }

    /// <summary>OAuth settings, preset endpoints and scopes included.</summary>
    public required OAuthSettings OAuth { get; init; }

    /// <summary>Sharing guard rails.</summary>
    public required SharePolicy Share { get; init; }

    /// <summary>The largest upload accepted, in bytes.</summary>
    public required long MaxUploadBytes { get; init; }

    /// <summary>HTTP timeout, or null for the client default.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>The root folder as a path, <c>/</c> for the whole drive.</summary>
    public string RootPath => Root.Count == 0 ? "/" : "/" + string.Join('/', Root);

    /// <summary>Whether the account grants <paramref name="right"/>.</summary>
    public bool Grants(DriveRights right) => (Rights & right) == right;
}
