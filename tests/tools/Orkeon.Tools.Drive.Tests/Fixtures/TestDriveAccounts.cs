using Orkeon.Tools.Drive.Configuration;

namespace Orkeon.Tools.Drive.Tests.Fixtures;

/// <summary>Declared accounts the tests start from, and their resolution.</summary>
internal static class TestDriveAccounts
{
    /// <summary>The environment variable the Google accounts name for their client secret.</summary>
    public const string GoogleSecretVariable = "ORKEON_TEST_DRIVE_GOOGLE_SECRET";

    /// <summary>A Google Drive account confined to <paramref name="root"/>.</summary>
    public static DriveAccountOptions Google(DriveRights rights = DriveRights.Read | DriveRights.Write | DriveRights.Organize, string? root = "Orkeon/agents")
    {
        var options = new DriveAccountOptions
        {
            Provider = DriveProvider.GoogleDrive,
            Rights = rights,
            Root = root,
            Auth = { ClientId = "drive-client.apps.googleusercontent.com", ClientSecretEnvVar = GoogleSecretVariable },
        };
        return options;
    }

    /// <summary>A personal OneDrive account confined to <paramref name="root"/>.</summary>
    public static DriveAccountOptions OneDrive(DriveRights rights = DriveRights.Read | DriveRights.Write | DriveRights.Organize, string? root = "Orkeon/agents", string? tenant = null)
    {
        var options = new DriveAccountOptions
        {
            Provider = DriveProvider.OneDrive,
            Rights = rights,
            Root = root,
            Auth = { ClientId = "00000000-aaaa-bbbb-cccc-dddddddddddd", Tenant = tenant },
        };
        return options;
    }

    /// <summary>Resolves <paramref name="options"/> under <paramref name="name"/>, failing the test on any problem.</summary>
    public static ResolvedDriveAccount Resolve(string name, DriveAccountOptions options)
    {
        var resolution = DriveAccountResolver.Resolve(name, options);
        Assert.True(resolution.Account is not null, string.Join("; ", resolution.Problems));
        return resolution.Account!;
    }

    /// <summary>An environment holding the Google client secret and nothing else.</summary>
    public static SecretEnvironment SecretEnvironment() =>
        new(name => name == GoogleSecretVariable ? "google-client-secret" : null);
}
