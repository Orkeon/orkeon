namespace Orkeon.Tools.Abstractions.Auth;

/// <summary>Why an OAuth exchange failed, in the terms a tool family translates into its own codes.</summary>
public enum OAuthFailure
{
    /// <summary>The account's settings lack what the flow needs (an endpoint, for instance).</summary>
    InvalidConfiguration,

    /// <summary>The identity provider refused the request or the credentials.</summary>
    AuthenticationFailed,

    /// <summary>No usable token: an interactive sign-in is needed.</summary>
    LoginRequired,

    /// <summary>The identity provider could not be reached or answered something unreadable.</summary>
    ServerError,
}

/// <summary>
/// An OAuth failure, its message written for the operator. The tool families catch it and throw
/// their own exception with the same message, so an agent sees one error vocabulary per family.
/// </summary>
#pragma warning disable S3925 // BinaryFormatter serialization is obsolete in .NET 10 (SYSLIB0051); ISerializable pattern not required
public sealed class OAuthException : Exception
#pragma warning restore S3925
{
    /// <summary>Creates the exception with its failure kind and actionable message.</summary>
    public OAuthException(OAuthFailure failure, string message) : base(message) => Failure = failure;

    /// <summary>Creates the exception with its failure kind, message and cause.</summary>
    public OAuthException(OAuthFailure failure, string message, Exception innerException)
        : base(message, innerException) => Failure = failure;

    /// <summary>Parameterless form for serializers; prefer the coded overloads.</summary>
    public OAuthException() : base("The OAuth exchange failed.") => Failure = OAuthFailure.ServerError;

    /// <summary>Message-only form; the kind defaults to <see cref="OAuthFailure.ServerError"/>.</summary>
    public OAuthException(string message) : base(message) => Failure = OAuthFailure.ServerError;

    /// <summary>Message-and-cause form; the kind defaults to <see cref="OAuthFailure.ServerError"/>.</summary>
    public OAuthException(string message, Exception innerException)
        : base(message, innerException) => Failure = OAuthFailure.ServerError;

    /// <summary>What went wrong.</summary>
    public OAuthFailure Failure { get; }
}
