namespace Orkeon.Studio.Core.Process;

/// <summary>
/// The standard input of a running child process, line by line.
/// <para>
/// Handed to the caller through <see cref="ProcessLaunchRequest.OnInputReady"/> once the
/// child has started. Lines are written in UTF-8 and flushed one by one, which is what a
/// line-oriented protocol (one JSON document per line) needs to make progress.
/// </para>
/// </summary>
public interface IProcessInputWriter
{
    /// <summary>
    /// Writes one line and flushes it. Returns false when the pipe is gone — the child
    /// exited or closed its stdin — which the caller treats as "no more questions", not
    /// as an error to surface.
    /// </summary>
    bool TryWriteLine(string line);

    /// <summary>
    /// Closes the child's stdin, signalling end-of-input. Safe to call more than once,
    /// and after the child exited.
    /// </summary>
    void Close();
}
