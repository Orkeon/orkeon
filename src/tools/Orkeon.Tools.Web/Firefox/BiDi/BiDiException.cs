#pragma warning disable CA1032, CA1064 // internal to the tool, one domain constructor: a code and its message

using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Web.Firefox.BiDi;

/// <summary>
/// A command the browser refused, did not answer, or could not receive. An expected tool error: the
/// agent reads its code and message, not a stack trace.
/// </summary>
internal sealed class BiDiException : Exception, IExpectedToolError
{
    /// <summary>Code of a command sent on a closed connection, or pending when it closed.</summary>
    public const string ConnectionClosed = "connection closed";

    /// <summary>Code of a command the browser did not answer in time.</summary>
    public const string Timeout = "timeout";

    /// <summary>Code the browser gives a command that names an element the page no longer holds.</summary>
    public const string NoSuchNode = "no such node";

    public BiDiException(string code, string message, Exception? innerException = null)
        : base(string.IsNullOrEmpty(message) ? code : $"{code}: {message}", innerException)
    {
        Code = code;
    }

    /// <summary>The WebDriver error code (<c>no such node</c>, <c>invalid argument</c>...), or one of the constants above.</summary>
    public string Code { get; }
}
