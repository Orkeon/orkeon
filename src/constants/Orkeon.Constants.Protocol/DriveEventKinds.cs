namespace Orkeon.Constants.Protocol;

/// <summary>
/// The event kinds <c>orkeon drive login --events jsonl</c> and <c>orkeon drive check --events
/// jsonl</c> write for whatever drives them — Orkeon Studio's Drive tab.
/// <para>
/// The protocol of <see cref="EmailEventKinds"/>, spelt for the drive family: one JSON document
/// per line in the envelope of <see cref="RunEventKinds"/>; the driver writes the address the
/// browser ended on as one plain line on the verb's standard input, and closes that input to
/// abort the sign-in. <see cref="All"/> is the set a consumer asserts against.
/// </para>
/// </summary>
public static class DriveEventKinds
{
    /// <summary>
    /// The device sign-in (OneDrive) is open: <c>verification_uri</c> is the page to open,
    /// <c>user_code</c> the code to type there, <c>expires_in</c> the seconds the code lives.
    /// </summary>
    public const string LoginDeviceCode = "drive.login.device_code";

    /// <summary>
    /// The loopback sign-in (Google Drive) is open: <c>authorization_uri</c> is the page to open
    /// in a browser. The verb then waits for the browser, or for the address it ended on.
    /// </summary>
    public const string LoginAuthorizationUrl = "drive.login.authorization_url";

    /// <summary>
    /// The line the driver wrote is not the address the browser ended on: <c>message</c> says
    /// why, without repeating the line. The sign-in goes on waiting.
    /// </summary>
    public const string LoginRedirectRejected = "drive.login.redirect_rejected";

    /// <summary>The tokens of <c>account</c> are stored: the last line of a sign-in that succeeded.</summary>
    public const string LoginCompleted = "drive.login.completed";

    /// <summary>
    /// <c>orkeon drive check --events jsonl</c> connected and listed the root folder of
    /// <c>account</c>: <c>root_path</c>, <c>root_name</c>, <c>items</c> (the first page's count),
    /// <c>more</c>, and <c>summary</c>, the sentence the verb prints without the option.
    /// </summary>
    public const string CheckCompleted = "drive.check.completed";

    /// <summary>
    /// The sign-in or the check was refused or failed — the run stream's kind: <c>code</c> (the
    /// drive error code, such as <c>LoginRequired</c>), <c>message</c> and <c>recoverable</c>.
    /// </summary>
    public const string Error = RunEventKinds.Error;

    /// <summary>Every kind <c>orkeon drive login --events jsonl</c> writes.</summary>
    public static IReadOnlyList<string> Login { get; } = [LoginDeviceCode, LoginAuthorizationUrl, LoginRedirectRejected, LoginCompleted, Error];

    /// <summary>Every kind <c>orkeon drive check --events jsonl</c> writes: the verdict, or the refusal.</summary>
    public static IReadOnlyList<string> Check { get; } = [CheckCompleted, Error];

    /// <summary>Every kind of the two verbs, each once.</summary>
    public static IReadOnlyList<string> All { get; } = [LoginDeviceCode, LoginAuthorizationUrl, LoginRedirectRejected, LoginCompleted, CheckCompleted, Error];
}
