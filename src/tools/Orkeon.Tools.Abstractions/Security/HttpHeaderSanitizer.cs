using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Orkeon.Tools.Abstractions.Security;

/// <summary>
/// Sanitizes HTTP headers to prevent header injection attacks and remove dangerous headers.
/// </summary>
public sealed partial class HttpHeaderSanitizer
{
    private readonly ILogger<HttpHeaderSanitizer> _logger;

    private static readonly HashSet<string> ForbiddenHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host",
        "Transfer-Encoding",
        "Content-Length",
        "Connection",
        "Upgrade",
        "Proxy-Authorization",
        "Proxy-Connection",
        "TE",
        "Trailer",
        "Cookie",
        "Set-Cookie"
    };

    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "X-Api-Key",
        "X-Auth-Token"
    };

    /// <summary>
    /// Initializes a new instance of <see cref="HttpHeaderSanitizer"/> with an optional logger.
    /// </summary>
    /// <param name="logger">Optional logger for security events.</param>
    public HttpHeaderSanitizer(ILogger<HttpHeaderSanitizer>? logger = null)
    {
        _logger = logger ?? NullLogger<HttpHeaderSanitizer>.Instance;
    }

    /// <summary>
    /// Sanitizes headers by removing forbidden headers, detecting CRLF injection,
    /// and warning about sensitive headers.
    /// </summary>
    public HeaderSanitizationResult SanitizeHeaders(IDictionary<string, string> headers)
    {
        var sanitized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();

        if (headers == null || headers.Count == 0)
        {
            return new HeaderSanitizationResult(sanitized, warnings);
        }

        foreach (var (key, value) in headers)
        {
            // Check for forbidden headers
            if (ForbiddenHeaders.Contains(key))
            {
                LogRemovedForbiddenHeader(key);
                warnings.Add($"Forbidden header '{key}' was removed");
                continue;
            }

            // Check for CRLF injection in key or value
            if (ContainsCRLF(key) || ContainsCRLF(value))
            {
                LogRemovedCrlfHeader(key);
                warnings.Add($"Header '{key}' was removed due to CRLF injection attempt");
                continue;
            }

            // Warn about sensitive headers
            if (SensitiveHeaders.Contains(key))
            {
                LogSensitiveHeaderDetected(key);
                warnings.Add($"Sensitive header '{key}' is being sent - ensure this is intentional");
            }

            sanitized[key] = value;
        }

        return new HeaderSanitizationResult(sanitized, warnings);
    }

    private static bool ContainsCRLF(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        // Check for literal CRLF characters
        if (value.Contains('\r', StringComparison.Ordinal) || value.Contains('\n', StringComparison.Ordinal))
            return true;

        // Check for URL-encoded CRLF
        if (value.Contains("%0d", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("%0a", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Security: Removed forbidden header '{Header}'")]
    private partial void LogRemovedForbiddenHeader(string header);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Security: Removed header '{Header}' due to CRLF injection attempt")]
    private partial void LogRemovedCrlfHeader(string header);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Security: Sensitive header '{Header}' detected")]
    private partial void LogSensitiveHeaderDetected(string header);
}

/// <summary>
/// Result of header sanitization containing sanitized headers and any warnings generated.
/// </summary>
public record HeaderSanitizationResult(
    IReadOnlyDictionary<string, string> SanitizedHeaders,
    IReadOnlyList<string> Warnings);
