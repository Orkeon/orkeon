namespace Orkeon.Studio.Core.Validation;

/// <summary>How a <see cref="ValidationMessage"/> should be surfaced by a UI.</summary>
public enum ValidationSeverity
{
    /// <summary>Advice; nothing is wrong.</summary>
    Information,

    /// <summary>The configuration is usable but will behave in a way the user may not expect.</summary>
    Warning,

    /// <summary>The configuration would be rejected at runtime.</summary>
    Error,
}

/// <summary>
/// One validation finding. UI-agnostic on purpose: the three Studio front-ends render
/// the same list, keyed by <see cref="Code"/>.
/// </summary>
public sealed record ValidationMessage
{
    /// <summary>Severity of the finding.</summary>
    public required ValidationSeverity Severity { get; init; }

    /// <summary>Stable identifier (see <see cref="ValidationCodes"/>), for UI keying and tests.</summary>
    public required string Code { get; init; }

    /// <summary>Human-readable message.</summary>
    public required string Text { get; init; }

    /// <summary>Configuration path or mount entry the finding is about, when applicable.</summary>
    public string? Path { get; init; }

    /// <summary>Creates an informational message.</summary>
    public static ValidationMessage Information(string code, string text, string? path = null) =>
        new() { Severity = ValidationSeverity.Information, Code = code, Text = text, Path = path };

    /// <summary>Creates a warning message.</summary>
    public static ValidationMessage Warning(string code, string text, string? path = null) =>
        new() { Severity = ValidationSeverity.Warning, Code = code, Text = text, Path = path };

    /// <summary>Creates an error message.</summary>
    public static ValidationMessage Error(string code, string text, string? path = null) =>
        new() { Severity = ValidationSeverity.Error, Code = code, Text = text, Path = path };
}

/// <summary>Stable codes carried by <see cref="ValidationMessage.Code"/>.</summary>
public static class ValidationCodes
{
    /// <summary>
    /// No <c>Llm</c> section: the runtime degrades silently to the echo provider.
    /// Named after the onboarding trap the runtime warning itself is tracked under.
    /// </summary>
    public const string LlmSectionMissing = "WIN-01";

    /// <summary>The document is not well-formed JSON.</summary>
    public const string MalformedJson = "STUDIO-JSON";

    /// <summary>A known key holds a value of the wrong JSON type.</summary>
    public const string InvalidFieldType = "STUDIO-TYPE";

    /// <summary>The API key is stored in clear text instead of an environment variable.</summary>
    public const string InlineApiKey = "STUDIO-LLM-APIKEY";

    /// <summary>
    /// An <c>ApiKey</c> written as a <c>${NAME}</c> placeholder, which the run never expands and
    /// refuses at start (STUDIO-55): its variable belongs in <c>ApiKeyEnvVar</c>.
    /// </summary>
    public const string LlmApiKeyPlaceholder = "STUDIO-LLM-APIKEY-PLACEHOLDER";

    /// <summary>
    /// An entry of <c>Llm:Profiles</c> named <c>default</c> (or blank) — the <c>Llm</c> section's own
    /// name, which the run refuses at start (STUDIO-55).
    /// </summary>
    public const string LlmProfileReservedName = "STUDIO-LLM-PROFILE-NAME";

    /// <summary><c>Orkeon:Rag:Profile</c> names no known profile.</summary>
    public const string UnknownRagProfile = "STUDIO-RAG-PROFILE";

    /// <summary>
    /// <c>Orkeon:Rag:LlmProfile</c> names an LLM profile the file's <c>Llm:Profiles</c> does not
    /// define (STUDIO-48): the host refuses to start on it.
    /// </summary>
    public const string UnknownRagLlmProfile = "STUDIO-RAG-LLM-PROFILE";

    /// <summary>No mount is declared.</summary>
    public const string MountsEmpty = "STUDIO-MOUNT-EMPTY";

    /// <summary>A mount entry does not parse.</summary>
    public const string MountFormat = "STUDIO-MOUNT-FORMAT";

    /// <summary>A mount's physical path does not exist on disk.</summary>
    public const string MountPathMissing = "STUDIO-MOUNT-PATH";

    /// <summary>Two mounts claim the same virtual path.</summary>
    public const string MountVirtualCollision = "STUDIO-MOUNT-COLLISION";

    /// <summary>One mount id carried by several entries — an id names one entry (VFS-90).</summary>
    public const string MountIdDuplicate = "STUDIO-MOUNT-ID";

    /// <summary>
    /// A virtual root declared by several entries that all carry an id: legitimate, a team or
    /// <c>--mount-id</c> picks one per run (VFS-90, information); or one folder declared twice
    /// under one root (warning).
    /// </summary>
    public const string MountSharedRoot = "STUDIO-MOUNT-SHARED";

    /// <summary>An MCP server identifier is empty or carries characters the binder would mangle (STUDIO-21).</summary>
    public const string McpServerIdInvalid = "STUDIO-MCP-ID";

    /// <summary>An MCP server names a transport the runtime does not know.</summary>
    public const string McpTransportUnknown = "STUDIO-MCP-TRANSPORT";

    /// <summary>A stdio MCP server has no command to launch.</summary>
    public const string McpCommandMissing = "STUDIO-MCP-COMMAND";

    /// <summary>An HTTP (sse) MCP server has no absolute http(s) URL.</summary>
    public const string McpUrlInvalid = "STUDIO-MCP-URL";

    /// <summary>An MCP server's environment block carries what looks like a secret, in clear text.</summary>
    public const string McpEnvLooksSecret = "STUDIO-MCP-ENV-SECRET";

    /// <summary>An e-mail account name the engine refuses (STUDIO-66): the account is set aside.</summary>
    public const string EmailName = "STUDIO-MAIL-NAME";

    /// <summary>
    /// Two e-mail account names equal but for the case — with <see cref="EmailTwin"/>, the e-mail
    /// findings that are an error: the JSON configuration refuses a key written twice without regard
    /// to case, so no run reads the file. A warning when the two share no key, which the run reads
    /// as one account.
    /// </summary>
    public const string EmailDuplicate = "STUDIO-MAIL-DUPLICATE";

    /// <summary>
    /// Two keys equal but for the case in one object of <c>Orkeon:Tools:Email</c> (<c>Provider</c>
    /// and <c>provider</c>) — an error when both set one same key, which the JSON configuration
    /// refuses: no run reads the file. A warning when they are objects that share no key, which the
    /// run reads as one while Studio reads and edits one of them only.
    /// </summary>
    public const string EmailTwin = "STUDIO-MAIL-TWIN";

    /// <summary>A key no e-mail account carries: the engine sets the account aside.</summary>
    public const string EmailKey = "STUDIO-MAIL-KEY";

    /// <summary>A value of an e-mail account the engine cannot read (a misspelt right, a port in words).</summary>
    public const string EmailValue = "STUDIO-MAIL-VALUE";

    /// <summary>An e-mail account without an address, or with a text that is none.</summary>
    public const string EmailAddress = "STUDIO-MAIL-ADDRESS";

    /// <summary>An e-mail account that grants no right.</summary>
    public const string EmailRights = "STUDIO-MAIL-RIGHTS";

    /// <summary>The servers of an e-mail account: host, port, protocol, security, timeout.</summary>
    public const string EmailServer = "STUDIO-MAIL-SERVER";

    /// <summary>How an e-mail account signs in: method, variable names, client id, tenant.</summary>
    public const string EmailAuth = "STUDIO-MAIL-AUTH";

    /// <summary>The sending side of an e-mail account: recipients, quotas, <c>Send</c> without an outgoing server.</summary>
    public const string EmailSend = "STUDIO-MAIL-SEND";

    /// <summary><c>Orkeon:Tools:Email:DefaultAccount</c> names no declared account.</summary>
    public const string EmailDefault = "STUDIO-MAIL-DEFAULT";

    /// <summary>
    /// <c>Orkeon:Tools:Email:Screening:WithholdRejected</c> is neither true nor false: the engine sets
    /// every account aside until it is.
    /// </summary>
    public const string EmailScreening = "STUDIO-MAIL-SCREENING";
}
