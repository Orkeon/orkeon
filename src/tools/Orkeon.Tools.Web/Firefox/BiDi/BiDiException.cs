#pragma warning disable CA1032 // one domain constructor: a code and its message

using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Web.Firefox.BiDi;

/// <summary>
/// A command the browser refused, did not answer, or could not receive. An expected tool error: the
/// agent reads its code and message, not a stack trace.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S3925", Justification = "False positive: binary serialization of exceptions is obsolete in .NET 10 (SYSLIB0051), so no ISerializable constructor or GetObjectData is added.")]
public sealed class BiDiException : Exception, IExpectedToolError
{
    /// <summary>Code of a command sent on a closed connection, or pending when it closed.</summary>
    public const string ConnectionClosed = "connection closed";

    /// <summary>Code of a command the browser did not answer in time.</summary>
    public const string Timeout = "timeout";

    /// <summary>Code the browser gives a command that names an element the page no longer holds.</summary>
    public const string NoSuchNode = "no such node";

    /// <summary>Code the browser gives a command that names a tab or frame it no longer holds.</summary>
    public const string NoSuchFrame = "no such frame";

    /// <summary>Creates the error of a command: its WebDriver code, the browser message and the cause.</summary>
    public BiDiException(string code, string message, Exception? innerException = null)
        : base(string.IsNullOrEmpty(message) ? code : $"{code}: {message}", innerException)
    {
        Code = code;
    }

    /// <summary>The WebDriver error code (<c>no such node</c>, <c>invalid argument</c>...), or one of the constants above.</summary>
    public string Code { get; }
}
