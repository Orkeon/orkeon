using System.Globalization;
using System.Text;
using Orkeon.Infrastructure.Security;

namespace Orkeon.Cli.Commands.Scripting.Shell;

/// <summary>
/// The output of an operator's shell command as the session will keep it, held in a bounded buffer: the whole output
/// while it fits in <see cref="OperatorShell.MaxSessionOutputCharacters"/> characters, past that its first
/// <see cref="HeadCharacters"/> and its last <see cref="TailCharacters"/> — never the middle, so <c>! yes</c> run until
/// the timeout keeps a few pages, not every line. <see cref="TotalCharacters"/> counts every character, for the cut.
/// Not thread-safe: the caller serialises the appends.
/// </summary>
internal sealed class OperatorShellOutput
{
    /// <summary>The characters a cut output keeps from its start.</summary>
    public const int HeadCharacters = 8_000;

    /// <summary>The characters a cut output keeps from its end.</summary>
    public const int TailCharacters = 2_000;

    /// <summary>The characters of the tail kept before masking: the margin lets a secret cut at its start still be recognised.</summary>
    private const int TailBufferCharacters = 2 * TailCharacters;

    private readonly StringBuilder _head = new();
    private readonly StringBuilder _tail = new();

    /// <summary>Every character appended, line breaks included.</summary>
    public long TotalCharacters { get; private set; }

    /// <summary>The characters the buffer holds now.</summary>
    public int RetainedCharacters => _head.Length + _tail.Length;

    /// <summary>Appends one line of output and its line break.</summary>
    public void AppendLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        TotalCharacters += line.Length + 1;
        var room = OperatorShell.MaxSessionOutputCharacters - _head.Length;
        if (_tail.Length == 0 && line.Length + 1 <= room)
        {
            _head.Append(line).Append('\n');
            return;
        }

        if (_tail.Length == 0 && room > 0)
        {
            _head.Append(line, 0, Math.Min(room, line.Length));
            line = line.Length > room ? line[room..] : string.Empty;
        }

        _tail.Append(line).Append('\n');
        if (_tail.Length > 2 * TailBufferCharacters)
            _tail.Remove(0, _tail.Length - TailBufferCharacters);
    }

    /// <summary>
    /// The text the session keeps: masked with the repository's rule (<see cref="LogSanitizer"/>) before it is cut, so a
    /// cut never leaves the start of a secret the rule no longer recognises; a cut output says how long it was.
    /// </summary>
    public string Render()
    {
        if (_tail.Length == 0)
        {
            var whole = LogSanitizer.SanitizeString(_head.ToString().TrimEnd('\n'));
            return whole.Length <= OperatorShell.MaxSessionOutputCharacters
                ? whole
                : Cut(whole, whole, whole.Length);
        }

        var head = LogSanitizer.SanitizeString(_head.ToString());
        var tail = LogSanitizer.SanitizeString(_tail.ToString().TrimEnd('\n'));
        return Cut(head, tail, TotalCharacters);
    }

    private static string Cut(string head, string tail, long total) =>
        head[..Math.Min(HeadCharacters, head.Length)].TrimEnd('\n')
        + $"\n[output cut: {total.ToString(CultureInfo.InvariantCulture)} characters, the first {HeadCharacters.ToString(CultureInfo.InvariantCulture)} and the last {TailCharacters.ToString(CultureInfo.InvariantCulture)} kept]\n"
        + tail[Math.Max(0, tail.Length - TailCharacters)..].TrimStart('\n');
}
