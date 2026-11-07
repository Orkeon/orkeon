using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Orkeon.Domain.Exceptions;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Resilience;

namespace Orkeon.Infrastructure.LLMs.Profiles;

/// <summary>How a failed model call is classed.</summary>
internal enum LlmFailureKind
{
    /// <summary>Not a failure, or one nothing can be said about: it is never moved to another profile.</summary>
    None,
    /// <summary>The request never got an answer: connection refused, reset, DNS, a stream cut before any answer.</summary>
    Transport,
    /// <summary>A 5xx.</summary>
    ServerError,
    /// <summary>A 429, or a rate-limit window longer than the call accepts to wait.</summary>
    RateLimited,
    /// <summary>The HTTP timeout, a 408 or the stream's idle bound elapsed.</summary>
    Timeout,
    /// <summary>Every profile of a nested fallback chain failed.</summary>
    Exhausted,
    /// <summary>The provider refused the request itself: a 4xx, a blocked content.</summary>
    Refused,
}

/// <summary>
/// The one classification of failures (FWK-05), shared by the retry policy
/// (<see cref="ResiliencePolicies"/>, through <see cref="IsTransientStatus"/>) and the fallback chain
/// (<see cref="FallbackLlmProvider"/>). A failure arrives two ways: an exception, or a response whose
/// <see cref="LlmResponse.Error"/> and <see cref="LlmResponse.ErrorType"/> the provider filled.
/// </summary>
internal static partial class LlmFailureClassifier
{
    /// <summary>The error type a chain writes when every profile failed.</summary>
    public const string ExhaustedErrorType = "FallbackExhausted";

    /// <summary>The error type of a call a decision stage blocked.</summary>
    private const string BlockedErrorType = "DecisionBlocked";

    /// <summary>Classes the exception a call failed with. A caller's own cancellation is not one: the chain checks its token first.</summary>
    public static LlmFailureKind Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (ResiliencePolicies.IsHttpClientTimeout(exception) || exception is System.TimeoutException)
            return LlmFailureKind.Timeout;
        return exception switch
        {
            LlmRateLimitException => LlmFailureKind.RateLimited,
            HttpRequestException { StatusCode: { } status } => ClassifyStatus(status),
            HttpRequestException or SocketException or IOException => LlmFailureKind.Transport,
            _ => LlmFailureKind.None,
        };
    }

    /// <summary>Classes the answer of a call that may have failed in-band; <see cref="LlmFailureKind.None"/> for an answer.</summary>
    public static LlmFailureKind Classify(LlmResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.Error is not { } error)
            return LlmFailureKind.None;

        switch (response.ErrorType)
        {
            case ExhaustedErrorType:
                return LlmFailureKind.Exhausted;
            case BlockedErrorType:
                return LlmFailureKind.Refused;
            case "TaskCanceledException" or "TimeoutException" or "OperationCanceledException" or "StreamIdleTimeout":
                return LlmFailureKind.Timeout;
            case "StreamTruncated" or "HttpRequestException" or "SocketException" or "IOException":
                return LlmFailureKind.Transport;
            case "APIError":
                return StatusOf(error) is { } status ? ClassifyStatus(status) : LlmFailureKind.None;
            default:
                return LlmFailureKind.None;
        }
    }

    /// <summary>True for the statuses a retry may cure and that a fallback takes over once it cannot: 408, 429, 5xx.</summary>
    public static bool IsTransientStatus(HttpStatusCode status) =>
        (int)status >= 500 || status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests;

    /// <summary>Whether a failure of this class moves the call to the next profile of a chain.</summary>
    public static bool FailsOver(LlmFailureKind kind) =>
        kind is LlmFailureKind.Transport or LlmFailureKind.ServerError or LlmFailureKind.RateLimited
            or LlmFailureKind.Timeout or LlmFailureKind.Exhausted;

    /// <summary>The word the event and the log use for a class.</summary>
    public static string Reason(LlmFailureKind kind) => kind switch
    {
        LlmFailureKind.Transport => "transport",
        LlmFailureKind.ServerError => "server_error",
        LlmFailureKind.RateLimited => "rate_limited",
        LlmFailureKind.Timeout => "timeout",
        LlmFailureKind.Exhausted => "exhausted",
        LlmFailureKind.Refused => "refused",
        _ => "none",
    };

    /// <summary>
    /// The exception a failed answer stands for, for the callers whose contract is to throw (a text
    /// stream): it classes the same as the answer did.
    /// </summary>
    public static Exception ToException(LlmResponse response)
    {
        var message = response.Error ?? "The call failed.";
        var kind = Classify(response);
        return kind switch
        {
            LlmFailureKind.Timeout => new System.TimeoutException(message),
            LlmFailureKind.Transport or LlmFailureKind.Exhausted => new HttpRequestException(message),
            LlmFailureKind.ServerError or LlmFailureKind.RateLimited or LlmFailureKind.Refused when StatusOf(message) is { } status =>
                new HttpRequestException(message, null, status),
            _ => new InvalidOperationException(message),
        };
    }

    private static LlmFailureKind ClassifyStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.RequestTimeout => LlmFailureKind.Timeout,
        HttpStatusCode.TooManyRequests => LlmFailureKind.RateLimited,
        _ when (int)status >= 500 => LlmFailureKind.ServerError,
        _ when (int)status >= 400 => LlmFailureKind.Refused,
        _ => LlmFailureKind.None,
    };

    /// <summary>The status a provider wrote in its error line: <c>"X API error: ServiceUnavailable - body"</c>.</summary>
    private static HttpStatusCode? StatusOf(string error)
    {
        var match = ApiErrorStatus().Match(error);
        return match.Success && Enum.TryParse<HttpStatusCode>(match.Groups[1].Value, ignoreCase: true, out var status)
            ? status
            : null;
    }

    [GeneratedRegex(@"API error:\s*(\w+)\s*-", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ApiErrorStatus();
}
