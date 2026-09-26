namespace Orkeon.Tools.Email;

/// <summary>Why an e-mail operation was refused or failed.</summary>
public enum EmailErrorCode
{
    /// <summary>No account is configured at all.</summary>
    NotConfigured,

    /// <summary>The call named an account that does not exist, or none when several do.</summary>
    UnknownAccount,

    /// <summary>The account's settings are invalid.</summary>
    InvalidConfiguration,

    /// <summary>The account does not grant the right the operation needs.</summary>
    RightDenied,

    /// <summary>The account's backend cannot do this (POP3 has no folders, for instance).</summary>
    Unsupported,

    /// <summary>An OAuth account has no usable token: an interactive sign-in is needed.</summary>
    LoginRequired,

    /// <summary>A named secret (environment variable) is not set.</summary>
    CredentialMissing,

    /// <summary>A recipient is outside the account's allow-list.</summary>
    RecipientNotAllowed,

    /// <summary>The account's sending quota for this hour is used up.</summary>
    QuotaExceeded,

    /// <summary>The message is larger than the backend accepts.</summary>
    TooLarge,

    /// <summary>The folder does not exist.</summary>
    FolderNotFound,

    /// <summary>The message does not exist (any more).</summary>
    MessageNotFound,

    /// <summary>The call's arguments are invalid.</summary>
    InvalidRequest,

    /// <summary>The server refused the credentials.</summary>
    AuthenticationFailed,

    /// <summary>The server could not be reached or answered with an error.</summary>
    ServerError,
}

/// <summary>
/// An e-mail failure an agent or an operator can act on. Its message is written for them: it
/// is the only text the agent loop forwards (<c>Error: …</c>), so it says what to do.
/// </summary>
public sealed class EmailToolException : Exception
{
    /// <summary>Creates the exception with its code and actionable message.</summary>
    public EmailToolException(EmailErrorCode code, string message) : base(message) => Code = code;

    /// <summary>Creates the exception with its code, message and cause.</summary>
    public EmailToolException(EmailErrorCode code, string message, Exception innerException)
        : base(message, innerException) => Code = code;

    /// <summary>Parameterless form for serializers; prefer the coded overloads.</summary>
    public EmailToolException() : base("The e-mail operation failed.") => Code = EmailErrorCode.ServerError;

    /// <summary>Message-only form; the code defaults to <see cref="EmailErrorCode.ServerError"/>.</summary>
    public EmailToolException(string message) : base(message) => Code = EmailErrorCode.ServerError;

    /// <summary>Message-and-cause form; the code defaults to <see cref="EmailErrorCode.ServerError"/>.</summary>
    public EmailToolException(string message, Exception innerException)
        : base(message, innerException) => Code = EmailErrorCode.ServerError;

    /// <summary>What went wrong, for callers that branch on it.</summary>
    public EmailErrorCode Code { get; }
}
