namespace Orkeon.Studio.Core.Process;

/// <summary>Which of the child process' two output channels a line came from.</summary>
public enum ProcessOutputChannel
{
    /// <summary>Standard output.</summary>
    StandardOutput,

    /// <summary>Standard error — where the CLI writes its warnings and fault messages.</summary>
    StandardError,
}

/// <summary>
/// One line of child-process output, delivered while the process is still running.
/// The timestamp is captured at delivery so a log panel can order the two channels.
/// </summary>
/// <param name="Channel">Originating channel.</param>
/// <param name="Text">The line, without its trailing newline.</param>
/// <param name="TimestampUtc">When the line was read.</param>
public sealed record ProcessOutputLine(ProcessOutputChannel Channel, string Text, DateTimeOffset TimestampUtc)
{
    /// <summary>Creates a line stamped with the current UTC time.</summary>
    public static ProcessOutputLine Now(ProcessOutputChannel channel, string text) =>
        new(channel, text, DateTimeOffset.UtcNow);
}
