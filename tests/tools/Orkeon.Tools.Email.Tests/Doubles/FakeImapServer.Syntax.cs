using System.Globalization;
using System.Text;

namespace Orkeon.Tools.Email.Tests.Doubles;

/// <summary>IMAP syntax of the fake: reading commands with literals, tokens, sets, patterns, quoting.</summary>
internal sealed partial class FakeImapServer
{
    private const char LiteralMarker = '\u0001';

    /// <summary>The kinds of token a command is made of.</summary>
    private enum TokenKind
    {
        Atom,
        String,
        Literal,
        List,
    }

    /// <summary>One token: an atom, a quoted string, a literal (with its bytes) or a parenthesized list.</summary>
    private sealed record Token(TokenKind Kind, string Text, byte[]? Bytes, List<Token> Items);

    /// <summary>A command as read: its text with literal markers, the literals, and a display form.</summary>
    private sealed record RawCommand(string Text, List<byte[]> Literals, string Display);

    private static async Task<RawCommand?> ReadCommandAsync(MailConnection connection, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var display = new StringBuilder();
        var literals = new List<byte[]>();
        while (true)
        {
            var line = await connection.ReadLineAsync(cancellationToken);
            if (line is null)
                return null;

            if (!TryLiteralSuffix(line, out var cut, out var size, out var synchronizing))
            {
                text.Append(line);
                display.Append(line);
                return new RawCommand(text.ToString(), literals, display.ToString());
            }

            text.Append(line, 0, cut);
            display.Append(line);
            if (synchronizing)
                await connection.WriteLineAsync("+ Ready for literal data", cancellationToken);
            literals.Add(await connection.ReadExactAsync(size, cancellationToken));
            text.Append(LiteralMarker).Append((literals.Count - 1).ToString(CultureInfo.InvariantCulture)).Append(LiteralMarker);
        }
    }

    private static bool TryLiteralSuffix(string line, out int cut, out int size, out bool synchronizing)
    {
        cut = line.LastIndexOf('{');
        size = 0;
        synchronizing = true;
        if (!line.EndsWith('}') || cut < 0)
            return false;

        var inside = line[(cut + 1)..^1];
        if (inside.EndsWith('+'))
        {
            synchronizing = false;
            inside = inside[..^1];
        }

        return int.TryParse(inside, NumberStyles.None, CultureInfo.InvariantCulture, out size);
    }

    private static List<Token> Tokenize(string text, List<byte[]> literals)
    {
        var position = 0;
        return ParseTokens(text, literals, ref position, closing: null);
    }

    private static List<Token> ParseTokens(string text, List<byte[]> literals, ref int position, char? closing)
    {
        var tokens = new List<Token>();
        while (position < text.Length)
        {
            var c = text[position];
            if (c == ' ')
            {
                position++;
            }
            else if (closing is { } close && c == close)
            {
                position++;
                return tokens;
            }
            else if (c == '(')
            {
                position++;
                tokens.Add(new Token(TokenKind.List, string.Empty, null, ParseTokens(text, literals, ref position, ')')));
            }
            else if (c == '"')
            {
                tokens.Add(ReadQuoted(text, ref position));
            }
            else if (c == LiteralMarker)
            {
                var end = text.IndexOf(LiteralMarker, position + 1);
                var bytes = literals[int.Parse(text[(position + 1)..end], CultureInfo.InvariantCulture)];
                tokens.Add(new Token(TokenKind.Literal, Encoding.UTF8.GetString(bytes), bytes, []));
                position = end + 1;
            }
            else
            {
                tokens.Add(ReadAtom(text, ref position));
            }
        }

        return tokens;
    }

    private static Token ReadQuoted(string text, ref int position)
    {
        var value = new StringBuilder();
        position++;
        while (position < text.Length && text[position] != '"')
        {
            if (text[position] == '\\' && position + 1 < text.Length)
                position++;
            value.Append(text[position]);
            position++;
        }

        position++;
        return new Token(TokenKind.String, value.ToString(), null, []);
    }

    private static Token ReadAtom(string text, ref int position)
    {
        var start = position;
        var brackets = 0;
        while (position < text.Length)
        {
            var c = text[position];
            if (c == '[')
                brackets++;
            else if (c == ']')
                brackets--;
            else if (brackets == 0 && (c == ' ' || c == '(' || c == ')'))
                break;
            position++;
        }

        return new Token(TokenKind.Atom, text[start..position], null, []);
    }

    /// <summary>The messages of <paramref name="folder"/> a sequence or UID set designates, by ascending UID.</summary>
    private static List<FakeImapMessage> ResolveSet(FakeImapFolder folder, string set, bool uid)
    {
        var messages = folder.Messages;
        var highest = uid ? (messages.Count == 0 ? 0u : messages[^1].Uid) : (uint)messages.Count;
        var selected = new HashSet<FakeImapMessage>();
        foreach (var range in set.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var bounds = range.Split(':');
            var low = Bound(bounds[0], highest);
            var high = bounds.Length > 1 ? Bound(bounds[1], highest) : low;
            if (low > high)
                (low, high) = (high, low);

            for (var index = 0; index < messages.Count; index++)
            {
                var key = uid ? messages[index].Uid : (uint)(index + 1);
                if (key >= low && key <= high)
                    selected.Add(messages[index]);
            }
        }

        return selected.OrderBy(m => m.Uid).ToList();
    }

    private static uint Bound(string value, uint highest) =>
        value == "*" ? highest : uint.Parse(value, CultureInfo.InvariantCulture);

    /// <summary>Whether an IMAP LIST pattern (<c>*</c> anything, <c>%</c> anything but '/') matches <paramref name="name"/>.</summary>
    private static bool NameMatches(string pattern, string name)
    {
        if (name.Equals("INBOX", StringComparison.OrdinalIgnoreCase) && pattern.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
            return true;
        return WildcardMatches(pattern, 0, name, 0);
    }

    private static bool WildcardMatches(string pattern, int p, string name, int n)
    {
        while (p < pattern.Length)
        {
            var c = pattern[p];
            if (c is '*' or '%')
            {
                for (var k = n; k <= name.Length; k++)
                {
                    if (WildcardMatches(pattern, p + 1, name, k))
                        return true;
                    if (k < name.Length && c == '%' && name[k] == '/')
                        return false;
                }

                return false;
            }

            if (n >= name.Length || c != name[n])
                return false;
            p++;
            n++;
        }

        return n == name.Length;
    }

    /// <summary>An IMAP quoted string (CR and LF folded to spaces).</summary>
    internal static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace('\r', ' ').Replace('\n', ' ') + "\"";

    /// <summary>A quoted string, or NIL.</summary>
    internal static string NString(string? value) => value is null ? "NIL" : Quote(value);
}
