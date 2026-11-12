namespace Orkeon.Domain.Exceptions;

/// <summary>A rate-limit window longer than the maximum wait the caller accepts.</summary>
public sealed class LlmRateLimitException : DomainException
{
    /// <summary>The wait the server asked for.</summary>
    public TimeSpan Wait { get; }

    /// <summary>Creates the exception without a message.</summary>
    public LlmRateLimitException() { }

    /// <summary>Creates the exception with a message.</summary>
    public LlmRateLimitException(string message) : base(message) { }

    /// <summary>Creates the exception with a message and its cause.</summary>
    public LlmRateLimitException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>Creates the exception with a message and the wait the server asked for.</summary>
    public LlmRateLimitException(string message, TimeSpan wait) : base(message) => Wait = wait;
}
