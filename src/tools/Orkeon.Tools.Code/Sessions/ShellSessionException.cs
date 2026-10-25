using System.Runtime.Serialization;

namespace Orkeon.Tools.Code.Sessions;

/// <summary>A shell session could not start, is closed, or its shell died while running a command.</summary>
[Serializable]
public sealed class ShellSessionException : Exception
{
    /// <summary>Initializes a new instance with no message.</summary>
    public ShellSessionException()
    {
    }

    /// <summary>Initializes a new instance with <paramref name="message"/>.</summary>
    /// <param name="message">What went wrong.</param>
    public ShellSessionException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with <paramref name="message"/> and its cause.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The cause.</param>
    public ShellSessionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The legacy serialization constructor of the <see cref="ISerializable"/> pattern.</summary>
    /// <param name="info">The serialized data.</param>
    /// <param name="context">The serialization context.</param>
    [Obsolete("Legacy formatter-based serialization is obsolete (SYSLIB0051).", DiagnosticId = "SYSLIB0051")]
    private ShellSessionException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
    }
}
