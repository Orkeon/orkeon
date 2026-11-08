namespace Orkeon.Infrastructure.LLMs.Replay;

/// <summary>
/// A replayed call the cassette holds no answer for. A replay never falls back on the network: the
/// call fails with this exception instead.
/// </summary>
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
