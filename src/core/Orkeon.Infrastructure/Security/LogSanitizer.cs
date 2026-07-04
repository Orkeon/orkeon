using System.Text.RegularExpressions;

namespace Orkeon.Infrastructure.Security;

/// <summary>
/// Sanitizes log output by masking known secret patterns such as API keys,
/// bearer tokens, and configuration values for sensitive keys.
/// <para>
/// <b>WARNING</b>: Never log raw exceptions that may contain API keys.
/// Always use <see cref="SanitizeString"/> or <see cref="CreateSanitizedException"/>
/// before logging or returning error messages.
/// </para>
/// </summary>
public static partial class LogSanitizer
{
    private const string RedactedPlaceholder = "***REDACTED***";

    /// <summary>
    /// HTTP header names whose values must always be redacted wholesale, regardless of
    /// their content. These carry credentials that are not necessarily detectable by the
    /// value-pattern regexes (e.g. Azure OpenAI's <c>api-key</c> is a bare hex string with
    /// no <c>sk-</c>/<c>Bearer</c> prefix). Names with a trailing <c>*</c> are treated as
    /// prefixes (e.g. <c>x-amz-*</c> matches <c>x-amz-security-token</c>).
    /// </summary>
    private static readonly string[] SensitiveHeaderNames =
    {
        "authorization",
        "proxy-authorization",
        "api-key",
        "x-api-key",
        "x-goog-api-key",
        "x-auth-token",
        "x-amz-*",
        "cookie",
        "set-cookie"
    };

    /// <summary>
    /// Determines whether the named HTTP header is considered sensitive and must have its
    /// value redacted in logs regardless of its content.
    /// </summary>
    /// <param name="headerName">The HTTP header name (case-insensitive).</param>
    /// <returns><c>true</c> if the header value must be redacted; otherwise <c>false</c>.</returns>
    public static bool IsSensitiveHeader(string? headerName)
    {
        if (string.IsNullOrWhiteSpace(headerName))
            return false;

        var name = headerName.Trim();
        foreach (var pattern in SensitiveHeaderNames)
        {
            if (pattern.EndsWith('*'))
            {
                var prefix = pattern[..^1];
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (name.Equals(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Sanitizes a single HTTP header value. If <paramref name="headerName"/> is a known
    /// sensitive header (see <see cref="IsSensitiveHeader"/>), the value is redacted
    /// wholesale; otherwise the value is passed through the pattern-based
    /// <see cref="SanitizeString"/> so embedded secrets are still masked.
    /// </summary>
    /// <param name="headerName">The HTTP header name (case-insensitive).</param>
    /// <param name="headerValue">The header value to sanitize.</param>
    /// <returns>The redacted or pattern-sanitized header value.</returns>
    public static string SanitizeHeaderValue(string? headerName, string headerValue)
    {
        if (IsSensitiveHeader(headerName))
            return RedactedPlaceholder;

        return SanitizeString(headerValue);
    }

    // OpenAI keys: sk- followed by 20+ alphanumeric chars (also matches sk-proj- variants)
    [GeneratedRegex(@"sk-[a-zA-Z0-9\-]{20,}", RegexOptions.Compiled)]
    private static partial Regex OpenAIKeyPattern();

    // Anthropic keys: sk-ant- followed by 20+ alphanumeric chars
    [GeneratedRegex(@"sk-ant-[a-zA-Z0-9\-]{20,}", RegexOptions.Compiled)]
    private static partial Regex AnthropicKeyPattern();

    // GitHub personal access tokens: ghp_ followed by alphanumeric chars
    [GeneratedRegex(@"ghp_[a-zA-Z0-9]{20,}", RegexOptions.Compiled)]
    private static partial Regex GitHubTokenPattern();

    // GitHub fine-grained tokens: github_pat_ followed by alphanumeric chars
    [GeneratedRegex(@"github_pat_[a-zA-Z0-9_]{20,}", RegexOptions.Compiled)]
    private static partial Regex GitHubFineGrainedTokenPattern();

    // Bearer tokens: Bearer followed by a long token
    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.Compiled)]
    private static partial Regex BearerTokenPattern();

    // Key-value patterns: api_key, apikey, secret, password, token, credential, auth
    // followed by separator and value
    [GeneratedRegex(@"(?<=(api_?key|secret|password|token|credential|auth)[""']?\s*[:=]\s*[""']?)[A-Za-z0-9\-._~+/]{8,}", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex KeyValuePattern();

    /// <summary>
    /// Replaces known secret patterns in the text with a masked version
    /// showing only the first 3 and last 3 characters.
    /// </summary>
    /// <param name="text">The text to sanitize.</param>
    /// <returns>The sanitized text, or the original value if null or empty.</returns>
    public static string? Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Order matters: more specific patterns first
        text = AnthropicKeyPattern().Replace(text, MaskMatch);
        text = OpenAIKeyPattern().Replace(text, MaskMatch);
        text = GitHubFineGrainedTokenPattern().Replace(text, MaskMatch);
        text = GitHubTokenPattern().Replace(text, MaskMatch);
        text = BearerTokenPattern().Replace(text, MaskBearerMatch);
        text = KeyValuePattern().Replace(text, MaskMatch);

        return text;
    }

    /// <summary>
    /// Sanitizes a string by replacing all known secret patterns with <c>***REDACTED***</c>.
    /// This is the primary method to use before logging any string that may contain credentials.
    /// </summary>
    /// <param name="input">The string to sanitize.</param>
    /// <returns>The sanitized string with secrets replaced by <c>***REDACTED***</c>,
    /// or the original value if null or empty.</returns>
    public static string SanitizeString(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var sanitized = input;

        // Order matters: more specific patterns first
        sanitized = AnthropicKeyPattern().Replace(sanitized, RedactedPlaceholder);
        sanitized = OpenAIKeyPattern().Replace(sanitized, RedactedPlaceholder);
        sanitized = GitHubFineGrainedTokenPattern().Replace(sanitized, RedactedPlaceholder);
        sanitized = GitHubTokenPattern().Replace(sanitized, RedactedPlaceholder);
        sanitized = BearerTokenPattern().Replace(sanitized, RedactBearerMatch);
        sanitized = KeyValuePattern().Replace(sanitized, RedactedPlaceholder);

        return sanitized;
    }

    /// <summary>
    /// Sanitizes an exception message and stack trace, returning a safe string representation.
    /// Use this when you need a full sanitized dump of the exception for diagnostics.
    /// </summary>
    /// <param name="ex">The exception to sanitize.</param>
    /// <returns>A sanitized string representation of the exception, or empty if null.</returns>
    public static string SanitizeException(Exception ex)
    {
        if (ex == null)
            return string.Empty;

        var message = SanitizeString(ex.Message);
        var stackTrace = SanitizeString(ex.StackTrace ?? "");
        var innerException = ex.InnerException != null
            ? SanitizeException(ex.InnerException)
            : null;

        var sanitized = $"{ex.GetType().Name}: {message}\n{stackTrace}";
        if (innerException != null)
            sanitized += $"\n--- Inner Exception: {innerException}";

        return sanitized;
    }

    /// <summary>
    /// Creates a <see cref="SanitizedException"/> with a sanitized message, suitable
    /// for logging or re-throwing. The original exception is preserved as the inner
    /// exception so its type and stack trace remain available for diagnostics, while
    /// the sanitized message (with secrets redacted) is safe to surface.
    /// </summary>
    /// <param name="ex">The original exception containing potentially sensitive data.</param>
    /// <returns>A new <see cref="SanitizedException"/> with <c>[SANITIZED]</c> prefix,
    /// redacted message, and the original exception preserved as inner.</returns>
    public static SanitizedException CreateSanitizedException(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        var sanitizedMessage = SanitizeString(ex.Message);
        return new SanitizedException($"[SANITIZED] {sanitizedMessage}", ex);
    }

    private static string MaskMatch(Match match)
    {
        var value = match.Value;
        if (value.Length < 8)
            return "***";
        return value[..3] + "..." + value[^3..];
    }

    private static string MaskBearerMatch(Match match)
    {
        var value = match.Value;
        // Keep "Bearer " prefix, mask the token part
        const string prefix = "Bearer ";
        if (value.Length <= prefix.Length + 6)
            return "Bearer ***";

        var token = value[prefix.Length..];
        return prefix + token[..3] + "..." + token[^3..];
    }

    private static string RedactBearerMatch(Match match)
    {
        return $"Bearer {RedactedPlaceholder}";
    }
}
