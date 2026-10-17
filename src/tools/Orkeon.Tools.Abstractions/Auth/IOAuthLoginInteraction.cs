namespace Orkeon.Tools.Abstractions.Auth;

/// <summary>
/// How an interactive OAuth sign-in talks to the person at the terminal. Shared by the tool
/// families that sign in to a cloud account: <c>orkeon email login</c> and <c>orkeon drive login</c>
/// drive the same conversation.
/// </summary>
public interface IOAuthLoginInteraction
{
    /// <summary>
    /// Tells the user to open <paramref name="verificationUri"/> and type <paramref name="userCode"/>
    /// (device sign-in). The code lives <paramref name="expiresIn"/>: past that, the sign-in fails
    /// and has to be started again.
    /// </summary>
    Task ShowDeviceCodeAsync(string account, Uri verificationUri, string userCode, TimeSpan expiresIn, CancellationToken cancellationToken);

    /// <summary>Tells the user to open <paramref name="authorizationUri"/> in a browser (loopback sign-in).</summary>
    Task ShowAuthorizationUrlAsync(string account, Uri authorizationUri, CancellationToken cancellationToken);

    /// <summary>
    /// Reads a redirect address the user pasted — the fallback when the browser cannot reach this
    /// machine's loopback port (WSL, a container, a remote shell). Returns null when nothing can be read.
    /// It may block its thread until a line comes, as a console read does: the sign-in calls it
    /// apart from the wait for the browser.
    /// </summary>
    Task<string?> ReadRedirectAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Tells the user that the line just pasted is not the address the browser ended on, and
    /// <paramref name="reason"/> why: one short sentence, which never repeats what was pasted (an
    /// authorization code is a secret while it lives). The sign-in is not over: it goes on waiting
    /// for the browser, or for another line.
    /// </summary>
    Task ShowRedirectRejectedAsync(string account, string reason, CancellationToken cancellationToken);
}
