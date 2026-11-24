namespace Orkeon.Constants.Protocol;

/// <summary>
/// The event kinds <c>orkeon chat login --events jsonl</c> and <c>orkeon chat check --events
/// jsonl</c> write for whatever drives them.
/// <para>
/// The protocol of <see cref="EmailEventKinds"/>, spelt for the chat family: one JSON document
/// per line in the envelope of <see cref="RunEventKinds"/>; the driver writes the address the
/// browser ended on as one plain line on the verb's standard input, and closes that input to
/// abort the sign-in. <see cref="All"/> is the set a consumer asserts against.
/// </para>
/// </summary>
public static class ChatEventKinds
{
    /// <summary>
    /// The device sign-in (<c>--device-code</c>) is open: <c>verification_uri</c> is the page to open,
    /// <c>user_code</c> the code to type there, <c>expires_in</c> the seconds the code lives.
    /// </summary>
    public const string LoginDeviceCode = "chat.login.device_code";

    /// <summary>
    /// The loopback sign-in (the default) is open: <c>authorization_uri</c> is the page to open
    /// in a browser. The verb then waits for the browser, or for the address it ended on.
    /// </summary>
    public const string LoginAuthorizationUrl = "chat.login.authorization_url";

    /// <summary>
    /// The line the driver wrote is not the address the browser ended on: <c>message</c> says
    /// why, without repeating the line. The sign-in goes on waiting.
    /// </summary>
    public const string LoginRedirectRejected = "chat.login.redirect_rejected";

    /// <summary>The tokens of <c>account</c> are stored: the last line of a sign-in that succeeded.</summary>
    public const string LoginCompleted = "chat.login.completed";

    /// <summary>
    /// <c>orkeon chat check --events jsonl</c> connected as <c>user</c> of <c>account</c>:
    /// <c>ready</c>, <c>rights</c> (one <c>right</c>, <c>ready</c> and <c>missing</c> each), and
    /// <c>summary</c>, the text the verb prints without the option.
    /// </summary>
    public const string CheckCompleted = "chat.check.completed";

    /// <summary>
    /// The sign-in or the check was refused or failed — the run stream's kind: <c>code</c> (the
    /// chat error code, such as <c>SignInRequired</c>), <c>message</c> and <c>recoverable</c>.
    /// </summary>
    public const string Error = RunEventKinds.Error;

    /// <summary>Every kind <c>orkeon chat login --events jsonl</c> writes.</summary>
    public static IReadOnlyList<string> Login { get; } = [LoginDeviceCode, LoginAuthorizationUrl, LoginRedirectRejected, LoginCompleted, Error];

    /// <summary>Every kind <c>orkeon chat check --events jsonl</c> writes: the verdict, or the refusal.</summary>
    public static IReadOnlyList<string> Check { get; } = [CheckCompleted, Error];

    /// <summary>Every kind of the two verbs, each once.</summary>
    public static IReadOnlyList<string> All { get; } = [LoginDeviceCode, LoginAuthorizationUrl, LoginRedirectRejected, LoginCompleted, CheckCompleted, Error];
}
