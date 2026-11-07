using Orkeon.Tools.Abstractions.Auth;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>Records what an interactive sign-in would have told the person.</summary>
internal sealed class FakeOAuthLoginInteraction : IOAuthLoginInteraction
{
    private readonly List<(string Account, Uri VerificationUri, string UserCode)> _deviceCodes = [];

    /// <summary>The device codes shown, in order.</summary>
    public IReadOnlyList<(string Account, Uri VerificationUri, string UserCode)> DeviceCodes
    {
        get { lock (_deviceCodes) return [.. _deviceCodes]; }
    }

    public Task ShowDeviceCodeAsync(string account, Uri verificationUri, string userCode, TimeSpan expiresIn, CancellationToken cancellationToken)
    {
        lock (_deviceCodes)
            _deviceCodes.Add((account, verificationUri, userCode));
        return Task.CompletedTask;
    }

    public Task ShowAuthorizationUrlAsync(string account, Uri authorizationUri, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<string?> ReadRedirectAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

    public Task ShowRedirectRejectedAsync(string account, string reason, CancellationToken cancellationToken) => Task.CompletedTask;
}
