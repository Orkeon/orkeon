using Orkeon.Tests.Shared.Timing;
using Orkeon.Tools.Drive.Auth;
using Orkeon.Tools.Drive.Configuration;
using Orkeon.Tools.Drive.Constants;
using Orkeon.Tools.Drive.Drives;
using Orkeon.Tools.Drive.Drives.Google;
using Orkeon.Tools.Drive.Tests.Doubles;

namespace Orkeon.Tools.Drive.Tests.Fixtures;

/// <summary>
/// One account's engine over a scripted HTTP handler, with a fresh token already stored so that
/// every request goes straight to the API.
/// </summary>
internal sealed class DriveFixture : IDisposable
{
    /// <summary>The token every API request must carry.</summary>
    public const string AccessToken = "fresh-access-token";

    /// <summary>Creates the fixture for <paramref name="account"/>.</summary>
    public DriveFixture(ResolvedDriveAccount account)
    {
        Account = account;
        Http = new HttpClient(Handler, disposeHandler: false);
        OAuth = new OAuth2Client(Http, Time, (_, _) => Task.CompletedTask);
        Credentials = new DriveCredentialProvider(Store, OAuth, TestDriveAccounts.SecretEnvironment());
        Store.With(DriveCredentialProvider.TokenKey(account), new DriveTokenSet
        {
            AccessToken = AccessToken,
            RefreshToken = "stored-refresh-token",
            ExpiresAt = Time.GetUtcNow().AddHours(1),
        });
        Drive = account.Provider == DriveProvider.GoogleDrive
            ? new GoogleDrive(new DriveApiClient(Http, account, Credentials, DriveDefaults.GoogleApiHost, "Google Drive"))
            : new Orkeon.Tools.Drive.Drives.Graph.OneDrive(new DriveApiClient(Http, account, Credentials, DriveDefaults.GraphHost, "Microsoft Graph"));
    }

    /// <summary>The account.</summary>
    public ResolvedDriveAccount Account { get; }

    /// <summary>The scripted API and identity provider.</summary>
    public FakeHttpMessageHandler Handler { get; } = new();

    /// <summary>The client over <see cref="Handler"/>.</summary>
    public HttpClient Http { get; }

    /// <summary>The clock.</summary>
    public ManualTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero));

    /// <summary>The token store.</summary>
    public FakeDriveTokenStore Store { get; } = new();

    /// <summary>The OAuth client.</summary>
    public OAuth2Client OAuth { get; }

    /// <summary>The credential provider.</summary>
    public DriveCredentialProvider Credentials { get; }

    /// <summary>The engine under test.</summary>
    public IDrive Drive { get; }

    /// <summary>The bearer header every API request carries.</summary>
    public static string Bearer => $"Bearer {AccessToken}";

    /// <inheritdoc />
    public void Dispose()
    {
        Http.Dispose();
        Handler.Dispose();
    }
}
