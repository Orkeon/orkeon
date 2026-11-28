using System.Globalization;
using System.Text;
using Orkeon.Infrastructure.Security;

namespace Orkeon.Cli.Commands.Scripting.Shell;

/// <summary>
/// The output of an operator's shell command as the session will keep it, held in a bounded buffer of whole lines: the
/// whole output while it fits in <see cref="OperatorShell.MaxSessionOutputCharacters"/> characters, past that its first
/// <see cref="HeadCharacters"/> and its last <see cref="TailCharacters"/> — never the middle, so <c>! yes</c> run until
/// the timeout keeps a few pages, not every line. A line is never split while it is buffered, so the masking sees every
/// secret whole before the cut; a line longer than <see cref="OperatorShell.MaxSessionOutputCharacters"/> is masked on
/// its own when it comes and only its two ends are kept. <see cref="TotalCharacters"/> counts every character, for the
/// cut. Not thread-safe: the caller serialises the appends.
/// </summary>
internal sealed class OperatorShellOutput
{
    /// <summary>The characters a cut output keeps from its start.</summary>
    public const int HeadCharacters = 8_000;

    /// <summary>The characters a cut output keeps from its end.</summary>
    public const int TailCharacters = 2_000;

    /// <summary>The characters of whole lines the tail keeps at least: the margin keeps the line the cut falls in whole.</summary>
    private const int TailBufferCharacters = 2 * TailCharacters;

    private readonly StringBuilder _head = new();
    private readonly Queue<string> _tail = new();
    private int _tailLength;
    private bool _overflowed;
    private bool _middleDropped;

    /// <summary>Every character appended, line breaks included.</summary>
    public long TotalCharacters { get; private set; }

    /// <summary>The characters the buffer holds now.</summary>
    public int RetainedCharacters => _head.Length + _tailLength;

    /// <summary>Appends one line of output and its line break.</summary>
    public void AppendLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        TotalCharacters += line.Length + 1;
        if (line.Length > OperatorShell.MaxSessionOutputCharacters)
        {
            // Masked alone while whole, then only its ends are kept: the cut falls inside it anyway.
            var masked = LogSanitizer.SanitizeString(line);
            line = masked.Length <= OperatorShell.MaxSessionOutputCharacters
                ? masked
                : string.Concat(masked.AsSpan(0, HeadCharacters), "\n", masked.AsSpan(masked.Length - TailCharacters));
            _middleDropped |= masked.Length > OperatorShell.MaxSessionOutputCharacters;
        }

        if (!_overflowed && _head.Length + line.Length + 1 <= OperatorShell.MaxSessionOutputCharacters)
        {
            _head.Append(line).Append('\n');
            return;
        }

        _overflowed = true;
        if (_head.Length < HeadCharacters)
        {
            // The head holds whole lines up to the first HeadCharacters at least.
            _head.Append(line).Append('\n');
            return;
        }

        _tail.Enqueue(line);
        _tailLength += line.Length + 1;
        while (_tail.Count > 1 && _tailLength - (_tail.Peek().Length + 1) >= TailBufferCharacters)
        {
            _tailLength -= _tail.Dequeue().Length + 1;
            _middleDropped = true;
        }
    }

    /// <summary>
    /// The text the session keeps: masked with the repository's rule (<see cref="LogSanitizer"/>) before it is cut, so a
    /// cut never leaves the start of a secret the rule no longer recognises; a cut output says how long it was.
    /// </summary>
    public string Render()
    {
        if (!_middleDropped)
        {
            // The buffer holds the whole output: masked whole, then cut if it is still too long.
            var whole = LogSanitizer.SanitizeString(string.Concat(_head.ToString(), string.Join('\n', _tail)).TrimEnd('\n'));
            return whole.Length <= OperatorShell.MaxSessionOutputCharacters
                ? whole
                : Cut(whole, whole, TotalCharacters);
        }

        var head = LogSanitizer.SanitizeString(_head.ToString());
        var tail = LogSanitizer.SanitizeString(string.Join('\n', _tail).TrimEnd('\n'));
        return Cut(head, tail.Length > 0 ? tail : head.TrimEnd('\n'), TotalCharacters);
    }

    private static string Cut(string head, string tail, long total) =>
        head[..Math.Min(HeadCharacters, head.Length)].TrimEnd('\n')
        + $"\n[output cut: {total.ToString(CultureInfo.InvariantCulture)} characters, the first {HeadCharacters.ToString(CultureInfo.InvariantCulture)} and the last {TailCharacters.ToString(CultureInfo.InvariantCulture)} kept]\n"
        + tail[Math.Max(0, tail.Length - TailCharacters)..].TrimStart('\n');
}
