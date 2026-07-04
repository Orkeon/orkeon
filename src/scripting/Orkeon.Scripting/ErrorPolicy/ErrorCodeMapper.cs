using System.Net;
using Orkeon.Scripting.Exceptions;

namespace Orkeon.Scripting.ErrorPolicy;

/// <summary>
/// Maps a thrown <see cref="Exception"/> to one of the normalized DSL error codes
/// surfaced to <c>onError</c> handlers (cf. chapter 13 of the spec).
/// </summary>
public static class ErrorCodeMapper
{
#pragma warning disable CS1591
    public const string CodeRateLimit = "rate_limit";
    public const string CodeTimeout = "timeout";
    public const string CodeNetwork = "network";
    public const string CodeToolError = "tool_error";
    public const string CodeLlmError = "llm_error";
    public const string CodeStateMutation = "state_mutation";
    public const string CodeLockTimeout = "lock_timeout";
    public const string CodeValidation = "validation";
    public const string CodeAgentNotInCrew = "agent_not_in_crew";
    public const string CodeReceiveTimeout = "receive_timeout";
    public const string CodeUnknown = "unknown";

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

#pragma warning restore CS1591

    private static Exception Unwrap(Exception ex)
    {
        var current = ex;
        while (current.InnerException is not null && current is AggregateException)
            current = current.InnerException;
        return current;
    }
}
