namespace Orkeon.Tools.Drive.Tests.Doubles;

/// <summary>
/// Scripted terminal of a device-code sign-in: records the code it was told to show. It has
/// nothing to paste, the way a terminal with no input has none.
/// </summary>
public sealed class FakeOAuthLoginInteraction : IOAuthLoginInteraction
{
    /// <summary>The device sign-in shown, when one was.</summary>
    public (string Account, Uri VerificationUri, string UserCode)? DeviceCode { get; private set; }

    /// <summary>The authorization address shown, when one was.</summary>
    public Uri? AuthorizationUri { get; private set; }

    /// <inheritdoc />
    public Task ShowDeviceCodeAsync(string account, Uri verificationUri, string userCode, TimeSpan expiresIn, CancellationToken cancellationToken)
    {
        DeviceCode = (account, verificationUri, userCode);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ShowAuthorizationUrlAsync(string account, Uri authorizationUri, CancellationToken cancellationToken)
    {
        AuthorizationUri = authorizationUri;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> ReadRedirectAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

    /// <inheritdoc />
    public Task ShowRedirectRejectedAsync(string account, string reason, CancellationToken cancellationToken) => Task.CompletedTask;
}
