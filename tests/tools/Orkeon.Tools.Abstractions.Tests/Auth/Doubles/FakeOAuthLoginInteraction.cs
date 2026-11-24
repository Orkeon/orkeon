using System.Web;
using Orkeon.Tools.Abstractions.Auth;

namespace Orkeon.Tools.Abstractions.Tests.Auth.Doubles;

/// <summary>A user who signs in at once: the redirect of the address shown is pasted back.</summary>
internal sealed class FakeOAuthLoginInteraction : IOAuthLoginInteraction
{
    /// <summary>The address shown by the loopback flow.</summary>
    public Uri? Authorization { get; private set; }

    /// <summary>The code shown by the device flow.</summary>
    public string? UserCode { get; private set; }

    public Task ShowDeviceCodeAsync(string account, Uri verificationUri, string userCode, TimeSpan expiresIn, CancellationToken cancellationToken)
    {
        UserCode = userCode;
        return Task.CompletedTask;
    }

    public Task ShowAuthorizationUrlAsync(string account, Uri authorizationUri, CancellationToken cancellationToken)
    {
        Authorization = authorizationUri;
        return Task.CompletedTask;
    }

    public Task<string?> ReadRedirectAsync(CancellationToken cancellationToken)
    {
        var query = HttpUtility.ParseQueryString(Authorization!.Query);
        return Task.FromResult<string?>($"{query["redirect_uri"]}?code=code-1&state={query["state"]}");
    }

    public Task ShowRedirectRejectedAsync(string account, string reason, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(reason);
}
