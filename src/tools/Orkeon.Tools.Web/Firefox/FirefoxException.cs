#pragma warning disable CA1032, CA1064 // internal to the tool, one domain constructor: the message the agent reads

using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Web.Firefox;

/// <summary>
/// What the firefox tool could not do for a reason the agent can act on: the browser is not installed,
/// did not start, a node id is stale. An expected tool error: its message is the answer, not a stack trace.
/// </summary>
internal sealed class FirefoxException : Exception, IExpectedToolError
{
    public FirefoxException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
