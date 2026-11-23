using System.Web;

namespace Orkeon.Tools.Chat.Tests.Doubles;

/// <summary>
/// Scripted terminal of a sign-in: records the code or the address it was told to show, and pastes
/// back the redirect of that address, the way a user who signs in at once does.
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
    public Task<string?> ReadRedirectAsync(CancellationToken cancellationToken)
    {
        var query = HttpUtility.ParseQueryString(AuthorizationUri!.Query);
        return Task.FromResult<string?>($"{query["redirect_uri"]}?code=code-1&state={query["state"]}");
    }

    /// <inheritdoc />
    public Task ShowRedirectRejectedAsync(string account, string reason, CancellationToken cancellationToken) => Task.CompletedTask;
}
