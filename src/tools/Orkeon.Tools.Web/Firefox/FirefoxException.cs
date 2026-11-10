#pragma warning disable CA1032 // one domain constructor: the message the agent reads

using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Web.Firefox;

/// <summary>
/// What the firefox tool could not do for a reason the agent can act on: the browser is not installed,
/// did not start, a node id is stale. An expected tool error: its message is the answer, not a stack trace.
/// </summary>
public sealed class FirefoxException : Exception, IExpectedToolError
{
    /// <summary>Creates the error with the message the agent reads.</summary>
    public FirefoxException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
