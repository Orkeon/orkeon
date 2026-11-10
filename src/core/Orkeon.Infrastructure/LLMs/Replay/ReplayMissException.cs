namespace Orkeon.Infrastructure.LLMs.Replay;

/// <summary>
/// A replayed call the cassette holds no answer for. A replay never falls back on the network: the
/// call fails with this exception instead.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S3925", Justification = "False positive: binary serialization of exceptions is obsolete in .NET 10 (SYSLIB0051), so no ISerializable constructor or GetObjectData is added.")]
public sealed class ReplayMissException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    public ReplayMissException()
    {
    }

    /// <summary>Creates the exception with <paramref name="message"/>.</summary>
    /// <param name="message">What was missing.</param>
    public ReplayMissException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with <paramref name="message"/> and its cause.</summary>
    /// <param name="message">What was missing.</param>
    /// <param name="innerException">The cause.</param>
    public ReplayMissException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
