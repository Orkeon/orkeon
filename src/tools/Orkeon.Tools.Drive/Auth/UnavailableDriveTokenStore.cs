namespace Orkeon.Tools.Drive.Auth;

/// <summary>
/// The token store of a host that provides none. A drive account gets a message that says
/// where tokens come from instead of a silent failure.
/// </summary>
internal sealed class UnavailableDriveTokenStore : IDriveTokenStore
{
    private const string Message =
        "This host keeps no OAuth tokens for drive accounts. Use the runner of the orkeon CLI, which keeps them once a " +
        "drive account is declared and its token directory can be created (it warns at start when it cannot); a host " +
        "of its own registers an IDriveTokenStore.";

    /// <inheritdoc />
    public Task<DriveTokenSet?> ReadAsync(string key, CancellationToken cancellationToken) =>
        throw new DriveToolException(DriveErrorCode.NotConfigured, Message);

    /// <inheritdoc />
    public Task WriteAsync(string key, DriveTokenSet tokens, CancellationToken cancellationToken) =>
        throw new DriveToolException(DriveErrorCode.NotConfigured, Message);

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken) =>
        throw new DriveToolException(DriveErrorCode.NotConfigured, Message);
}
