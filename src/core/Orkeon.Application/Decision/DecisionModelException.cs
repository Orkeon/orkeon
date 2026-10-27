namespace Orkeon.Application.Decision;

/// <summary>
/// A decision provider refused or failed a request. The message names what to fix and never
/// carries the API key.
/// </summary>
public sealed class DecisionModelException : Exception
{
    /// <summary>Initializes a new instance of <see cref="DecisionModelException"/>.</summary>
    public DecisionModelException()
    {
    }

    /// <summary>Initializes a new instance of <see cref="DecisionModelException"/>.</summary>
    public DecisionModelException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of <see cref="DecisionModelException"/>.</summary>
    public DecisionModelException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new instance of <see cref="DecisionModelException"/>.</summary>
    /// <param name="message">What failed and what to fix.</param>
    /// <param name="statusCode">The HTTP status, null when no response came back.</param>
    /// <param name="isTransient">True when the same request may succeed later (rate limit, overload, network).</param>
    /// <param name="innerException">The cause, if any.</param>
    public DecisionModelException(string message, int? statusCode, bool isTransient, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        IsTransient = isTransient;
    }

    /// <summary>Gets the HTTP status, null when no response came back.</summary>
    public int? StatusCode { get; }

    /// <summary>Gets a value indicating whether the same request may succeed later.</summary>
    public bool IsTransient { get; }
}
