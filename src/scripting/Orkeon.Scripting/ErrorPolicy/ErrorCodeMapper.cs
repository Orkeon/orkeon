using System.Net;
using Orkeon.Scripting.Exceptions;

namespace Orkeon.Scripting.ErrorPolicy;

/// <summary>
/// Maps a thrown <see cref="Exception"/> to one of the normalized DSL error codes
/// surfaced to <c>onError</c> handlers (cf. chapter 13 of the spec).
/// </summary>
public static class ErrorCodeMapper
{
    /// <summary>Provider refused the call for rate limiting (HTTP 429).</summary>
    public const string CodeRateLimit = "rate_limit";

    /// <summary>The call exceeded its deadline without being cancelled by the caller.</summary>
    public const string CodeTimeout = "timeout";

    /// <summary>Transport failure reaching a remote endpoint.</summary>
    public const string CodeNetwork = "network";

    /// <summary>A tool invocation failed inside its own body.</summary>
    public const string CodeToolError = "tool_error";

    /// <summary>The LLM provider returned an error response.</summary>
    public const string CodeLlmError = "llm_error";

    /// <summary>Agent state was mutated outside a <c>state.with(...)</c> block.</summary>
    public const string CodeStateMutation = "state_mutation";

    /// <summary>A named lock could not be acquired within its deadline.</summary>
    public const string CodeLockTimeout = "lock_timeout";

    /// <summary>Arguments handed to the DSL failed validation.</summary>
    public const string CodeValidation = "validation";

    /// <summary>The addressed agent is not a member of the current crew.</summary>
    public const string CodeAgentNotInCrew = "agent_not_in_crew";

    /// <summary>A <c>queue.pop({ timeout })</c> expired before a value arrived.</summary>
    public const string CodeReceiveTimeout = "receive_timeout";

    /// <summary>Fallback for an exception no other code claims.</summary>
    public const string CodeUnknown = "unknown";

    /// <summary>
    /// Maps <paramref name="ex"/> (unwrapped through its aggregate layers) to the code
    /// an <c>onError</c> handler observes as <c>err.code</c>.
    /// </summary>
    /// <param name="ex">Exception raised by the failing body.</param>
    /// <returns>One of the <c>Code*</c> constants; <see cref="CodeUnknown"/> when nothing matches.</returns>
    public static string MapToCode(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        return Unwrap(ex) switch
        {
            HttpRequestException http when http.StatusCode == HttpStatusCode.TooManyRequests => CodeRateLimit,
            HttpRequestException => CodeNetwork,
            TaskCanceledException tce when tce.CancellationToken == default => CodeTimeout,
            TimeoutException => CodeTimeout,
            ReceiveTimeoutException => CodeReceiveTimeout,
            StateMutationOutsideWithException => CodeStateMutation,
            AgentNotInCrewException => CodeAgentNotInCrew,
            ArgumentException => CodeValidation,
            InvalidScriptException => CodeValidation,
            _ => CodeUnknown,
        };
    }

    private static Exception Unwrap(Exception ex)
    {
        var current = ex;
        while (current.InnerException is not null && current is AggregateException)
            current = current.InnerException;
        return current;
    }
}
