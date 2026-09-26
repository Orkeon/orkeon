using Orkeon.Tools.Email.Administration;

namespace Orkeon.Tools.Email.Tests.Doubles;

/// <summary>
/// A terminal nobody types into, read the way <c>Console.In</c> reads: the paste prompt blocks
/// its calling thread until <see cref="Dispose"/>, whatever its async signature, then answers
/// null. It records the authorization address the sign-in showed.
/// </summary>
public sealed class StubTerminalLoginInteraction : IEmailLoginInteraction, IDisposable
{
    private readonly TaskCompletionSource<Uri> _authorizationShown = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _input = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes with the authorization address once it is shown.</summary>
    public Task<Uri> AuthorizationShown => _authorizationShown.Task;

    /// <inheritdoc />
    public Task ShowDeviceCodeAsync(string account, Uri verificationUri, string userCode, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public Task ShowAuthorizationUrlAsync(string account, Uri authorizationUri, CancellationToken cancellationToken)
    {
        _authorizationShown.TrySetResult(authorizationUri);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1849", Justification = "The double blocks on purpose: it stands for Console.In, whose async read blocks its caller.")]
    public Task<string?> ReadRedirectAsync(CancellationToken cancellationToken)
    {
        // Deliberately synchronous, like a console read: the caller's thread waits here, and
        // the token is not honoured either.
        _ = _input.Task.Wait(TimeSpan.FromMinutes(1), CancellationToken.None);
        return Task.FromResult<string?>(null);
    }

    /// <summary>Ends the blocked read (end of input).</summary>
    public void Dispose() => _input.TrySetResult();
}
