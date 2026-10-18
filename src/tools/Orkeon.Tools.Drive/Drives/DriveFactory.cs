using System.Collections.Concurrent;
using Orkeon.Tools.Drive.Auth;
using Orkeon.Tools.Drive.Configuration;
using Orkeon.Tools.Drive.Constants;
using Orkeon.Tools.Drive.Drives.Google;

namespace Orkeon.Tools.Drive.Drives;

/// <summary>
/// Opens each account's drive once and keeps it: an engine caches the account's root folder and
/// the parents it has seen, which every root check reads again.
/// </summary>
internal sealed class DriveFactory : IDriveFactory
{
    private readonly DriveCredentialProvider _credentials;
    private readonly Func<HttpClient> _http;
    private readonly ConcurrentDictionary<string, IDrive> _drives = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the provider.</summary>
    /// <param name="credentials">Where the accounts' tokens come from.</param>
    /// <param name="http">Returns the HTTP client the engines call their API with.</param>
    public DriveFactory(DriveCredentialProvider credentials, Func<HttpClient> http)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(http);
        _credentials = credentials;
        _http = http;
    }

    /// <inheritdoc />
    public IDrive GetDrive(ResolvedDriveAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return _drives.GetOrAdd(account.Name, _ => Open(account));
    }

    private IDrive Open(ResolvedDriveAccount account) => account.Provider switch
    {
        DriveProvider.GoogleDrive => new GoogleDrive(new DriveApiClient(_http(), account, _credentials, DriveDefaults.GoogleApiHost, "Google Drive")),
        _ => new Graph.OneDrive(new DriveApiClient(_http(), account, _credentials, DriveDefaults.GraphHost, "Microsoft Graph")),
    };
}
