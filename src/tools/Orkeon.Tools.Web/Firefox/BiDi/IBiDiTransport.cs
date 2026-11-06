namespace Orkeon.Tools.Web.Firefox.BiDi;

/// <summary>
/// The channel a <see cref="BiDiConnection"/> talks to the browser through: whole JSON messages, one
/// text frame each. Disposing it closes the channel.
/// </summary>
internal interface IBiDiTransport : IAsyncDisposable
{
    /// <summary>Sends one message.</summary>
    Task SendAsync(string message, CancellationToken cancellationToken);

    /// <summary>Waits for the next message; <c>null</c> once the browser has closed the channel.</summary>
    Task<string?> ReceiveAsync(CancellationToken cancellationToken);
}
