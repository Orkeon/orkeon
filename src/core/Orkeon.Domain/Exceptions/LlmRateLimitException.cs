using System.Runtime.Serialization;

namespace Orkeon.Domain.Exceptions;

/// <summary>A rate-limit window longer than the maximum wait the caller accepts.</summary>
[Serializable]
public sealed class LlmRateLimitException : DomainException
{
    private const string WaitKey = "Wait";

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

    [Obsolete("Formatter-based serialization is obsolete.", DiagnosticId = "SYSLIB0051")]
    private LlmRateLimitException(SerializationInfo info, StreamingContext context)
        : base(info, context) => Wait = (TimeSpan)info.GetValue(WaitKey, typeof(TimeSpan))!;

    /// <inheritdoc />
    [Obsolete("Formatter-based serialization is obsolete.", DiagnosticId = "SYSLIB0051")]
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue(WaitKey, Wait);
    }
}
