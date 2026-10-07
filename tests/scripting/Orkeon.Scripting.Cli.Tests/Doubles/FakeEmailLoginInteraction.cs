using Orkeon.Tools.Email.Administration;

namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>
/// Stands in for the terminal of <c>orkeon email login</c>: records the device code the
/// sign-in showed, and never pastes a redirect.
/// </summary>
internal sealed class FakeEmailLoginInteraction : IEmailLoginInteraction
{
    /// <summary>The address the user was sent to, once a device code was shown.</summary>
    public Uri? VerificationUri { get; private set; }

    /// <summary>The code the user was asked to enter, once a device code was shown.</summary>
    public string? UserCode { get; private set; }

    /// <inheritdoc />
    public Task ShowDeviceCodeAsync(string account, Uri verificationUri, string userCode, TimeSpan expiresIn, CancellationToken cancellationToken)
    {
        VerificationUri = verificationUri;
        UserCode = userCode;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ShowAuthorizationUrlAsync(string account, Uri authorizationUri, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The Outlook preset signs in with a device code.");

    /// <inheritdoc />
    public Task<string?> ReadRedirectAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

    /// <inheritdoc />
    public Task ShowRedirectRejectedAsync(string account, string reason, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Nothing is ever pasted here.");
}
