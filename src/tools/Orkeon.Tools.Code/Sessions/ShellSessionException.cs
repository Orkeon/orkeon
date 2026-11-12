namespace Orkeon.Tools.Code.Sessions;

/// <summary>A shell session could not start, is closed, or its shell died while running a command.</summary>
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
}
