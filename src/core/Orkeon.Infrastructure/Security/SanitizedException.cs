namespace Orkeon.Infrastructure.Security;

/// <summary>
/// An exception whose message has been sanitized of secret patterns (API keys,
/// bearer tokens, credentials) while preserving the original exception as its
/// inner exception for diagnostics.
/// <para>
/// Created via <see cref="LogSanitizer.CreateSanitizedException(System.Exception)"/>.
/// Use this when re-throwing or wrapping an exception that may contain sensitive
/// data in its message: the sanitized message is safe to log, and the inner
/// exception preserves the original type and stack trace.
/// </para>
/// </summary>
public sealed class SanitizedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SanitizedException"/> class
    /// with a sanitized message.
    /// </summary>
    /// <param name="message">The sanitized message (secrets already redacted).</param>
    public SanitizedException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SanitizedException"/> class
    /// with a sanitized message and the original exception preserved as inner.
    /// </summary>
    /// <param name="message">The sanitized message (secrets already redacted).</param>
    /// <param name="innerException">The original exception to preserve.</param>
    public SanitizedException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SanitizedException"/> class.
    /// </summary>
    public SanitizedException()
    {
    }
}
