using System.Threading.Channels;
using Orkeon.Tools.Email.Administration;

namespace Orkeon.Scripting.Cli.Commands;

/// <summary>The three steps of a sign-in a driver is told about, as each verb's event writer spells them.</summary>
internal interface IOAuthLoginEvents
{
    /// <summary>The device sign-in is open.</summary>
    void LoginDeviceCode(Uri verificationUri, string userCode, TimeSpan expiresIn);

    /// <summary>The loopback sign-in is open.</summary>
    void LoginAuthorizationUrl(Uri authorizationUri);

    /// <summary>The line read on standard input is not the address the browser ended on.</summary>
    void LoginRedirectRejected(string message);
}

/// <summary>
/// The sign-in a program drives (STUDIO-70), shared by <c>orkeon email login</c> and
/// <c>orkeon drive login</c> with <c>--events jsonl</c>: each step is an event line, and the redirect
/// address is whatever line the driver writes on standard input — a line that is not one is
/// answered by an event too, never by silence. That input is read from the start, on a thread
/// of its own — a read of a redirected input blocks its caller —, so that its end is noticed
/// in the device sign-in too, where nothing is ever pasted.
/// </summary>
internal sealed class EventOAuthLoginInteraction : IEmailLoginInteraction
{
    private readonly IOAuthLoginEvents _events;
    private readonly Channel<string> _pasted = Channel.CreateUnbounded<string>();

    public EventOAuthLoginInteraction(IOAuthLoginEvents events, TextReader input, Action onInputClosed)
    {
        _events = events;
        _ = Task.Factory.StartNew(
            () => Pump(input, onInputClosed), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public Task ShowDeviceCodeAsync(string account, Uri verificationUri, string userCode, TimeSpan expiresIn, CancellationToken cancellationToken)
    {
        _events.LoginDeviceCode(verificationUri, userCode, expiresIn);
        return Task.CompletedTask;
    }

    public Task ShowAuthorizationUrlAsync(string account, Uri authorizationUri, CancellationToken cancellationToken)
    {
        _events.LoginAuthorizationUrl(authorizationUri);
        return Task.CompletedTask;
    }

    public async Task<string?> ReadRedirectAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _pasted.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    public Task ShowRedirectRejectedAsync(string account, string reason, CancellationToken cancellationToken)
    {
        _events.LoginRedirectRejected(reason);
        return Task.CompletedTask;
    }

    private void Pump(TextReader input, Action onInputClosed)
    {
        try
        {
            while (input.ReadLine() is { } line)
                _pasted.Writer.TryWrite(line);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The input broke rather than ended: the driver is gone all the same.
        }

        _pasted.Writer.TryComplete();
        onInputClosed();
    }
}
