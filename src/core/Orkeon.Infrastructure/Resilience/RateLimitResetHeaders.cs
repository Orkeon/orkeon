using System.Globalization;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace Orkeon.Infrastructure.Resilience;

/// <summary>
/// The rate-limit window a response announces through its reset headers: <c>Retry-After</c>,
/// <c>retry-after-ms</c> (milliseconds, sent by OpenAI and TypeSafe), the Anthropic RFC 3339 resets
/// and the OpenAI duration resets; the longest wins.
/// </summary>
public sealed partial class RateLimitResetHeaders
{
    private static readonly string[] _rfc3339Headers =
        ["anthropic-ratelimit-requests-reset", "anthropic-ratelimit-tokens-reset"];

    private static readonly string[] _durationHeaders =
        ["x-ratelimit-reset-requests", "x-ratelimit-reset-tokens"];

    private static readonly string[] _millisecondHeaders = ["retry-after-ms"];

    /// <summary>The reason reported to retry observers while a rate-limit window is waited out.</summary>
    public const string Reason = "rate-limit window";

    /// <summary>The longest wait the headers ask for; null when none is present.</summary>
    public TimeSpan? Wait { get; }

    private RateLimitResetHeaders(TimeSpan? wait) => Wait = wait;

    /// <summary>Parses the reset headers of a response; the longest wait wins.</summary>
    public static RateLimitResetHeaders Parse(HttpResponseHeaders headers, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(headers);
        TimeSpan? longest = null;

        void Offer(TimeSpan? candidate)
        {
            if (candidate is { } value && (longest is null || value > longest))
                longest = value < TimeSpan.Zero ? TimeSpan.Zero : value;
        }

        var retryAfter = headers.RetryAfter;
        Offer(retryAfter?.Delta ?? (retryAfter?.Date - now));

        foreach (var name in _rfc3339Headers)
        {
            if (TryFirstValue(headers, name, out var raw)
                && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var reset))
                Offer(reset - now);
        }

        foreach (var name in _durationHeaders)
        {
            if (TryFirstValue(headers, name, out var raw))
                Offer(ParseDuration(raw));
        }

        foreach (var name in _millisecondHeaders)
        {
            if (TryFirstValue(headers, name, out var raw)
                && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var milliseconds)
                && double.IsFinite(milliseconds))
                Offer(TimeSpan.FromMilliseconds(milliseconds));
        }

        return new RateLimitResetHeaders(longest);
    }

    /// <summary>
    /// The rate-limit window a 429, or a 529 with a reset header, announces; null otherwise.
    /// A window longer than <paramref name="maximum"/> is refused.
    /// </summary>
    /// <exception cref="Orkeon.Domain.Exceptions.LlmRateLimitException">The window exceeds <paramref name="maximum"/>.</exception>
    public static TimeSpan? WindowOf(HttpResponseMessage response, DateTimeOffset now, TimeSpan maximum)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.StatusCode is not (System.Net.HttpStatusCode.TooManyRequests or (System.Net.HttpStatusCode)529))
            return null;
        var window = Parse(response.Headers, now).Wait;
        if (window is { } announced && announced > maximum)
            throw new Orkeon.Domain.Exceptions.LlmRateLimitException(
                $"LLM {Reason} of {announced} exceeds the maximum wait of {maximum} (Llm:MaxRateLimitWait).",
                announced);
        return window;
    }

    private static bool TryFirstValue(HttpResponseHeaders headers, string name, out string value)
    {
        value = headers.TryGetValues(name, out var values) ? values.FirstOrDefault() ?? string.Empty : string.Empty;
        return value.Length > 0;
    }

    /// <summary>Reads an OpenAI duration such as <c>1s</c>, <c>6m0s</c> or <c>250ms</c>.</summary>
    private static TimeSpan? ParseDuration(string raw)
    {
        var matches = DurationPart().Matches(raw);
        if (matches.Count == 0 || string.Concat(matches.Select(m => m.Value)) != raw.Trim())
            return null;

        var total = TimeSpan.Zero;
        foreach (var groups in matches.Select(part => part.Groups))
        {
            var amount = double.Parse(groups[1].Value, CultureInfo.InvariantCulture);
            total += groups[2].Value switch
            {
                "ms" => TimeSpan.FromMilliseconds(amount),
                "h" => TimeSpan.FromHours(amount),
                "m" => TimeSpan.FromMinutes(amount),
                _ => TimeSpan.FromSeconds(amount),
            };
        }

        return total;
    }

    [GeneratedRegex(@"(\d+(?:\.\d+)?)(ms|h|m|s)", RegexOptions.CultureInvariant)]
    private static partial Regex DurationPart();
}
