using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Orkeon.Tools.Chat.Constants;

namespace Orkeon.Tools.Chat.Text;

/// <summary>
/// A transcript as an agent reads it: <c>[hh:mm:ss] speaker: text</c>, one line per speech.
/// </summary>
/// <param name="Lines">The lines, a long speech cut over several.</param>
/// <param name="Words">What is said, without the times and the names that come between.</param>
internal sealed partial record TranscriptText(IReadOnlyList<string> Lines, string Words)
{
    /// <summary>The lines of WebVTT: what is not a cue is dropped, and a speaker's cues in a row are one speech.</summary>
    public static TranscriptText FromVtt(string vtt)
    {
        ArgumentNullException.ThrowIfNull(vtt);
        var builder = new Builder();
        var cue = new StringBuilder();
        string? time = null;
        foreach (var row in Rows(vtt))
        {
            if (Timing(row) is { } start)
            {
                builder.Add(time, cue);
                time = start;
            }
            else if (string.IsNullOrWhiteSpace(row))
            {
                builder.Add(time, cue);
                time = null;
            }
            else if (time is not null)
            {
                cue.Append(cue.Length == 0 ? string.Empty : " ").Append(row);
            }
        }

        builder.Add(time, cue);
        return builder.Build();
    }

    /// <summary>The lines of a transcript without speakers.</summary>
    public static TranscriptText FromPlain(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = Rows(text).Select(Clean).Where(line => line.Length > 0).ToList();
        return new TranscriptText(lines.SelectMany(Cut).ToList(), string.Join(' ', lines));
    }

    private static string[] Rows(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    private static string Clean(string text) => ChatToolHelpers.OneLine(WebUtility.HtmlDecode(text)).Trim();

    // Empty for a timing this reader does not know, null for a row that is none.
    private static string? Timing(string row)
    {
        if (!row.Contains("-->", StringComparison.Ordinal))
            return null;
        var match = Offset().Match(row);
        return match.Success
            ? string.Create(CultureInfo.InvariantCulture, $"{match.Groups["sign"].Value}{(match.Groups["h"].Success ? match.Groups["h"].Value : "00")}:{match.Groups["m"].Value}:{match.Groups["s"].Value}")
            : string.Empty;
    }

    private static IEnumerable<string> Cut(string text)
    {
        for (var at = 0; at < text.Length;)
        {
            var length = Math.Min(ChatDefaults.MaxTranscriptLine, text.Length - at);
            if (at + length < text.Length && char.IsHighSurrogate(text[at + length - 1]))
                length--;
            yield return text.Substring(at, length);
            at += length;
        }
    }

    [GeneratedRegex(@"^[ \t]{0,8}(?<sign>-?)(?:(?<h>[0-9]{2,4}):)?(?<m>[0-9]{2}):(?<s>[0-9]{2})\.[0-9]{3}[ \t]{0,8}-->", RegexOptions.CultureInvariant)]
    private static partial Regex Offset();

    private sealed class Builder
    {
        private readonly List<string> _lines = [];
        private readonly StringBuilder _words = new();
        private readonly StringBuilder _speech = new();
        private string _speaker = string.Empty;
        private string _time = string.Empty;

        /// <summary>Adds a cue and empties it. Its voice tags tell who speaks, each until the next one.</summary>
        public void Add(string? time, StringBuilder cue)
        {
            var text = cue.ToString();
            cue.Clear();
            if (time is null || text.Length == 0)
                return;
            var speaker = string.Empty;
            var plain = new StringBuilder();
            var close = 0;
            for (var at = 0; at < text.Length;)
            {
                // One search for the end of a tag serves every bracket before it.
                if (text[at] == '<' && close >= 0 && close <= at)
                    close = text.IndexOf('>', at);
                if (text[at] != '<' || close <= at || !IsTag(text, at))
                {
                    plain.Append(text[at++]);
                    continue;
                }

                if (Voice(text.AsSpan(at + 1, close - at - 1)) is { } name)
                {
                    Say(time, speaker, plain);
                    speaker = name;
                }

                at = close + 1;
            }

            Say(time, speaker, plain);
        }

        public TranscriptText Build()
        {
            Flush();
            return new TranscriptText(_lines, _words.ToString());
        }

        private static bool IsTag(string text, int at) =>
            at + 1 < text.Length && (char.IsAsciiLetterOrDigit(text[at + 1]) || text[at + 1] == '/');

        private static string? Voice(ReadOnlySpan<char> tag)
        {
            if (tag.Length < 2 || tag[0] != 'v' || tag[1] is not (' ' or '\t' or '.'))
                return null;
            var space = tag.IndexOfAny(' ', '\t');
            return space < 0 ? null : Clean(tag[space..].ToString()).Replace(':', ' ');
        }

        private void Say(string time, string speaker, StringBuilder plain)
        {
            var text = Clean(plain.ToString());
            plain.Clear();
            if (text.Length == 0)
                return;
            _words.Append(_words.Length == 0 ? string.Empty : " ").Append(text);
            if (speaker != _speaker || _speech.Length == 0 || _speech.Length + text.Length >= ChatDefaults.MaxTranscriptLine)
            {
                Flush();
                (_speaker, _time) = (speaker, time);
            }

            _speech.Append(_speech.Length == 0 ? string.Empty : " ").Append(text);
        }

        private void Flush()
        {
            if (_speech.Length == 0)
                return;
            var name = ChatToolHelpers.Head(_speaker, ChatDefaults.MaxLabelChars);
            var prefix = (_time.Length > 0 ? $"[{_time}] " : string.Empty) + (name.Length > 0 ? $"{name}: " : string.Empty);
            _lines.AddRange(Cut(_speech.ToString()).Select(part => prefix + part));
            _speech.Clear();
        }
    }
}
