using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Chat;

/// <summary>Why a chat operation was refused or failed.</summary>
public enum ChatErrorCode
{
    /// <summary>No account is configured at all.</summary>
    NotConfigured,

    /// <summary>The call named an account that does not exist, or none when several do.</summary>
    UnknownAccount,

    /// <summary>The account's settings are invalid.</summary>
    InvalidConfiguration,

    /// <summary>The account does not grant the right the operation needs.</summary>
    RightMissing,

    /// <summary>The place is outside the account's allowed places.</summary>
    PlaceNotAllowed,

    /// <summary>The application lacks a permission, or the signed-in user may not do this.</summary>
    ConsentMissing,

    /// <summary>The account has no usable token: an interactive sign-in is needed.</summary>
    SignInRequired,

    /// <summary>The provider limits the rate of requests, or the account's hourly cap is reached.</summary>
    Throttled,

    /// <summary>The place, the message or the person does not exist (any more), or the account cannot see it.</summary>
    NotFound,

    /// <summary>The account's provider or identity cannot do this.</summary>
    NotSupportedByAccount,

    /// <summary>The call's arguments are invalid.</summary>
    InvalidRequest,

    /// <summary>The provider could not be reached or answered with an error.</summary>
    ServerError,
}

/// <summary>
/// A chat failure an agent or an operator can act on. Its message is written for them: it is
/// the only text the agent loop forwards (<c>Error: …</c>), so it says what to do.
/// </summary>
#pragma warning disable S3925 // BinaryFormatter serialization is obsolete in .NET 10 (SYSLIB0051); ISerializable pattern not required
public sealed class ChatToolException : Exception, IExpectedToolError
#pragma warning restore S3925
{
    /// <summary>Creates the exception with its code and actionable message.</summary>
    public ChatToolException(ChatErrorCode code, string message) : base(message) => Code = code;

    /// <summary>Creates the exception with its code, message and cause.</summary>
    public ChatToolException(ChatErrorCode code, string message, Exception innerException)
        : base(message, innerException) => Code = code;

    /// <summary>Parameterless form for serializers; prefer the coded overloads.</summary>
    public ChatToolException() : base("The chat operation failed.") => Code = ChatErrorCode.ServerError;

    /// <summary>Message-only form; the code defaults to <see cref="ChatErrorCode.ServerError"/>.</summary>
    public ChatToolException(string message) : base(message) => Code = ChatErrorCode.ServerError;

    /// <summary>Message-and-cause form; the code defaults to <see cref="ChatErrorCode.ServerError"/>.</summary>
    public ChatToolException(string message, Exception innerException)
        : base(message, innerException) => Code = ChatErrorCode.ServerError;

    /// <summary>What went wrong, for callers that branch on it.</summary>
    public ChatErrorCode Code { get; }
}
