using Microsoft.Extensions.Configuration;

namespace Orkeon.Tools.Drive.Configuration;

/// <summary>
/// What a host needs to know, before its services exist, to provide the OAuth token store:
/// whether any drive account is declared (every one signs in with OAuth2), and where the
/// operator wants the tokens.
/// </summary>
public static class DriveCredentialsLocation
{
    /// <summary>The virtual directory, under the host's credentials root, the drive tokens live in.</summary>
    public const string TokenSubdirectory = "drive";

    /// <summary>Whether the <c>Orkeon:Tools:Drive</c> section declares an account.</summary>
    /// <param name="driveSection">The <c>Orkeon:Tools:Drive</c> section.</param>
    public static bool NeedsTokenStore(IConfiguration driveSection)
    {
        ArgumentNullException.ThrowIfNull(driveSection);
        return Bind(driveSection).Accounts.Count > 0;
    }

    /// <summary>The operator's <c>CredentialsDirectory</c>, or null to use the host's default.</summary>
    /// <param name="driveSection">The <c>Orkeon:Tools:Drive</c> section.</param>
    public static string? ConfiguredDirectory(IConfiguration driveSection)
    {
        ArgumentNullException.ThrowIfNull(driveSection);
        var directory = Bind(driveSection).CredentialsDirectory;
        return string.IsNullOrWhiteSpace(directory) ? null : directory.Trim();
    }

    // Never throws: a value the binder cannot convert leaves its account out, reported when used.
    private static DriveToolsOptions Bind(IConfiguration driveSection)
    {
        var options = new DriveToolsOptions();
        DriveOptionsBinder.Bind(driveSection, options);
        return options;
    }
}
