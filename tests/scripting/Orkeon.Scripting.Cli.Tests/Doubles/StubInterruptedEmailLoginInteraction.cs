using Orkeon.Tools.Email.Administration;

namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>
/// Stands in for the terminal of <c>orkeon email login</c> while the operator presses Ctrl+C:
/// once the device code is shown it plays the key press through <see cref="Interrupt"/>, then
/// waits for the sign-in to be cancelled, as a user who never enters the code would leave it.
/// </summary>
internal sealed class StubInterruptedEmailLoginInteraction(Func<bool> interrupt) : IEmailLoginInteraction
{
    /// <summary>Plays the Ctrl+C; true when a handler of the verb consumed it.</summary>
    public Func<bool> Interrupt { get; } = interrupt;

    /// <summary>Whether the key press reached a handler the verb registered.</summary>
    public bool Consumed { get; private set; }

    /// <inheritdoc />
    public async Task ShowDeviceCodeAsync(string account, Uri verificationUri, string userCode, TimeSpan expiresIn, CancellationToken cancellationToken)
    {
        Consumed = Interrupt();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
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
