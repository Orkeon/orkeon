using Orkeon.Tools.Email.Administration;

namespace Orkeon.Tools.Email.Tests.Doubles;

/// <summary>
/// Scripted terminal of an interactive sign-in: records what it was told to show and answers
/// paste prompts from a queue of lines computed from the authorization address it was shown
/// (so a pasted redirect can echo the real state). With nothing queued it answers null, the way
/// a terminal with no input does.
/// </summary>
public sealed class FakeLoginInteraction : IEmailLoginInteraction
{
    private readonly TaskCompletionSource<Uri> _authorizationShown = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Queue<Func<Uri, string?>> _pastes = new();
    private readonly Lock _gate = new();

    /// <summary>The device sign-in shown, when one was.</summary>
    public (string Account, Uri VerificationUri, string UserCode)? DeviceCode { get; private set; }

    /// <summary>How long the device code shown was said to live.</summary>
    public TimeSpan? DeviceCodeExpiresIn { get; private set; }

    /// <summary>The account the loopback sign-in was shown for.</summary>
    public string? AuthorizationAccount { get; private set; }

    /// <summary>Completes with the authorization address once it is shown.</summary>
    public Task<Uri> AuthorizationShown => _authorizationShown.Task;

    /// <summary>How many times the paste prompt was read.</summary>
    public int PasteReads { get; private set; }

    /// <summary>Queues a pasted line, computed from the authorization address.</summary>
    public FakeLoginInteraction Paste(Func<Uri, string?> line)
    {
        lock (_gate)
            _pastes.Enqueue(line);
        return this;
    }

    /// <inheritdoc />
    public Task ShowDeviceCodeAsync(string account, Uri verificationUri, string userCode, TimeSpan expiresIn, CancellationToken cancellationToken)
    {
        DeviceCode = (account, verificationUri, userCode);
        DeviceCodeExpiresIn = expiresIn;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ShowAuthorizationUrlAsync(string account, Uri authorizationUri, CancellationToken cancellationToken)
    {
        AuthorizationAccount = account;
        _authorizationShown.TrySetResult(authorizationUri);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<string?> ReadRedirectAsync(CancellationToken cancellationToken)
    {
        var shown = await AuthorizationShown.WaitAsync(cancellationToken);
        Func<Uri, string?>? next;
        lock (_gate)
        {
            PasteReads++;
            next = _pastes.Count > 0 ? _pastes.Dequeue() : null;
        }

        return next?.Invoke(shown);
    }
}
