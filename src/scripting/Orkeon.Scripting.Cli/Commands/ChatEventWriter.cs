using Orkeon.Constants.Protocol;
using Orkeon.Scripting.Cli.Events;
using Orkeon.Tools.Chat.Administration;

namespace Orkeon.Scripting.Cli.Commands;

/// <summary>
/// The chat sign-in's and check's view of the shared event protocol: the envelope and the
/// emission come from <see cref="OrkeonEventWriter"/>, this type adds the typed lines of
/// <see cref="ChatEventKinds"/>. A sign-in belongs to no crew, so its events carry no identity.
/// </summary>
internal sealed class ChatEventWriter : OrkeonEventWriter, IOAuthLoginEvents
{
    /// <summary>What an <c>error</c> line carries as its code when the failure has no chat error code.</summary>
    public const string UnexpectedCode = "Unexpected";

    /// <summary>Creates a writer over <paramref name="output"/> (stdout in the CLI).</summary>
    public ChatEventWriter(TextWriter output, IOrkeonClock? clock = null)
        : base(output, clock)
    {
    }

    /// <summary>The device sign-in is open: the page to open, the code to type there, the seconds it lives.</summary>
    public void LoginDeviceCode(Uri verificationUri, string userCode, TimeSpan expiresIn)
    {
        ArgumentNullException.ThrowIfNull(verificationUri);
        Emit(ChatEventKinds.LoginDeviceCode, new
        {
            verification_uri = verificationUri.AbsoluteUri,
            user_code = userCode,
            expires_in = (long)expiresIn.TotalSeconds,
        });
    }

    /// <summary>The loopback sign-in is open: the page to open in a browser.</summary>
    public void LoginAuthorizationUrl(Uri authorizationUri)
    {
        ArgumentNullException.ThrowIfNull(authorizationUri);
        Emit(ChatEventKinds.LoginAuthorizationUrl, new { authorization_uri = authorizationUri.AbsoluteUri });
    }

    /// <summary>The line read on standard input is not the address the browser ended on; the sign-in goes on waiting.</summary>
    public void LoginRedirectRejected(string message) =>
        Emit(ChatEventKinds.LoginRedirectRejected, new { message });

    /// <summary>The tokens of <paramref name="account"/> are stored.</summary>
    public void LoginCompleted(string account) =>
        Emit(ChatEventKinds.LoginCompleted, new { account });

    /// <summary>The sign-in was refused or failed; the verb is about to exit on the code it has without events.</summary>
    public void Error(string code, string message) =>
        Emit(ChatEventKinds.Error, new { code, message, recoverable = false });

    /// <summary>The check connected: what each right of the account can do, and the text the verb prints without events.</summary>
    public void CheckCompleted(ChatCheckResult result, string summary)
    {
        ArgumentNullException.ThrowIfNull(result);
        Emit(ChatEventKinds.CheckCompleted, new
        {
            account = result.Account,
            user = result.User,
            ready = result.Ready,
            rights = result.Rights.Select(right => new { right = right.Right, ready = right.Ready, missing = right.Missing }).ToArray(),
            summary,
        });
    }
}
