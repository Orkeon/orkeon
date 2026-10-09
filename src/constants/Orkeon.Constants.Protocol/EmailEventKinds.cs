namespace Orkeon.Constants.Protocol;

/// <summary>
/// The event kinds <c>orkeon email login --events jsonl</c> and <c>orkeon email check --events
/// jsonl</c> write for whatever drives them (STUDIO-70).
/// <para>
/// Same protocol as the run stream — one JSON document per line, the envelope of
/// <see cref="RunEventKinds"/> — and the same reason to declare the vocabulary once: the CLI
/// signs the account in, Orkeon Studio shows what the person has to do meanwhile, and the two
/// cannot reference each other. The other direction is not made of events: the driver writes
/// the address the browser ended on as one plain line on the verb's standard input, and closes
/// that input to abort the sign-in. A kind one side spells differently is not an error anywhere
/// — the reader skips it — so the drift would show up as a panel that never says what to do.
/// <see cref="All"/> is the set a consumer asserts against.
/// </para>
/// </summary>
public static class EmailEventKinds
{
    /// <summary>
    /// The device sign-in (Microsoft) is open: <c>verification_uri</c> is the page to open,
    /// <c>user_code</c> the code to type there, <c>expires_in</c> the seconds the code lives.
    /// </summary>
    public const string LoginDeviceCode = "email.login.device_code";

    /// <summary>
    /// The loopback sign-in (Google) is open: <c>authorization_uri</c> is the page to open in a
    /// browser. The verb then waits for the browser to come back to this machine, or for the
    /// address it ended on, written as one line on standard input.
    /// </summary>
    public const string LoginAuthorizationUrl = "email.login.authorization_url";

    /// <summary>
    /// The line the driver wrote on standard input is not the address the browser ended on:
    /// <c>message</c> says why, in one sentence that never repeats the line. The sign-in is not
    /// over — the verb goes on waiting for the browser, or for another line.
    /// </summary>
    public const string LoginRedirectRejected = "email.login.redirect_rejected";

    /// <summary>The tokens of <c>account</c> are stored: the last line of a sign-in that succeeded.</summary>
    public const string LoginCompleted = "email.login.completed";

    /// <summary>
    /// <c>orkeon email check --events jsonl</c> connected, signed in and listed the folders of
    /// <c>account</c>: <c>folders</c> how many, <c>inbox_total</c> and <c>inbox_unread</c> the
    /// inbox counts when it has one, and <c>summary</c> the sentence the verb prints without the
    /// option — for a driver that shows the engine's words as printed.
    /// </summary>
    public const string CheckCompleted = "email.check.completed";

    /// <summary>
    /// The sign-in was refused or failed — the run stream's kind, with its shape: <c>code</c>
    /// (the e-mail error code, such as <c>CredentialMissing</c>), <c>message</c> and
    /// <c>recoverable</c>. The exit code is the one the verb has without events.
    /// </summary>
    public const string Error = RunEventKinds.Error;

    /// <summary>
    /// Every kind <c>orkeon email login --events jsonl</c> writes, so a consumer can assert it
    /// handles them all rather than discovering a gap as a step that silently never shows.
    /// </summary>
    public static IReadOnlyList<string> Login { get; } = [LoginDeviceCode, LoginAuthorizationUrl, LoginRedirectRejected, LoginCompleted, Error];

    /// <summary>Every kind <c>orkeon email check --events jsonl</c> writes: the verdict, or the refusal.</summary>
    public static IReadOnlyList<string> Check { get; } = [CheckCompleted, Error];

    /// <summary>Every kind of the two verbs, each once.</summary>
    public static IReadOnlyList<string> All { get; } = [LoginDeviceCode, LoginAuthorizationUrl, LoginRedirectRejected, LoginCompleted, CheckCompleted, Error];
}
