using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Drive;

/// <summary>Why a drive operation was refused or failed.</summary>
public enum DriveErrorCode
{
    /// <summary>No account is configured at all.</summary>
    NotConfigured,

    /// <summary>The call named an account that does not exist, or none when several do.</summary>
    UnknownAccount,

    /// <summary>The account's settings are invalid.</summary>
    InvalidConfiguration,

    /// <summary>The account does not grant the right the operation needs.</summary>
    RightDenied,

    /// <summary>The account's provider cannot do this (permanent deletion on a personal OneDrive, for instance).</summary>
    Unsupported,

    /// <summary>The account has no usable token: an interactive sign-in is needed.</summary>
    LoginRequired,

    /// <summary>A named secret (environment variable) is not set.</summary>
    CredentialMissing,

    /// <summary>A share recipient is outside the account's allow-list, or a public link is not allowed.</summary>
    RecipientNotAllowed,

    /// <summary>The file is larger than the account accepts.</summary>
    TooLarge,

    /// <summary>The item does not exist (any more), or lies outside the account's root folder.</summary>
    ItemNotFound,

    /// <summary>An item of that name already exists where it would go.</summary>
    Conflict,

    /// <summary>The call's arguments are invalid.</summary>
    InvalidRequest,

    /// <summary>The provider refused the credentials.</summary>
    AuthenticationFailed,

    /// <summary>The provider could not be reached or answered with an error.</summary>
    ServerError,
}

/// <summary>
/// A drive failure an agent or an operator can act on. Its message is written for them: it is
/// the only text the agent loop forwards (<c>Error: …</c>), so it says what to do.
/// </summary>
#pragma warning disable S3925 // BinaryFormatter serialization is obsolete in .NET 10 (SYSLIB0051); ISerializable pattern not required
public sealed class DriveToolException : Exception, IExpectedToolError
#pragma warning restore S3925
{
    /// <summary>Creates the exception with its code and actionable message.</summary>
    public DriveToolException(DriveErrorCode code, string message) : base(message) => Code = code;

    /// <summary>Creates the exception with its code, message and cause.</summary>
    public DriveToolException(DriveErrorCode code, string message, Exception innerException)
        : base(message, innerException) => Code = code;

    /// <summary>Parameterless form for serializers; prefer the coded overloads.</summary>
    public DriveToolException() : base("The drive operation failed.") => Code = DriveErrorCode.ServerError;

    /// <summary>Message-only form; the code defaults to <see cref="DriveErrorCode.ServerError"/>.</summary>
    public DriveToolException(string message) : base(message) => Code = DriveErrorCode.ServerError;

    /// <summary>Message-and-cause form; the code defaults to <see cref="DriveErrorCode.ServerError"/>.</summary>
    public DriveToolException(string message, Exception innerException)
        : base(message, innerException) => Code = DriveErrorCode.ServerError;

    /// <summary>What went wrong, for callers that branch on it.</summary>
    public DriveErrorCode Code { get; }
}
