using System.Globalization;
using System.Text;

namespace Orkeon.Tools.Email.Tests.Doubles;

/// <summary>
/// SEARCH of the fake: the RFC 3501 keys MailKit emits, plus a naive X-GM-RAW, answered as
/// <c>* SEARCH</c> or — for <c>SEARCH RETURN (…)</c>, which only a server advertising ESEARCH
/// accepts — as <c>* ESEARCH</c> (RFC 4731).
/// </summary>
internal sealed partial class FakeImapServer
{
    private async Task<bool> SearchAsync(ImapRequest request)
    {
        var tokens = request.Arguments;
        var index = 0;
        List<string>? returning = null;
        if (index + 1 < tokens.Count && tokens[index].Text.Equals("RETURN", StringComparison.OrdinalIgnoreCase) && tokens[index + 1].Kind == TokenKind.List)
        {
            if (!Advertises("ESEARCH"))
                throw new NotSupportedException("SEARCH RETURN needs the ESEARCH extension, which this server does not advertise");
            returning = tokens[index + 1].Items.Select(item => item.Text.ToUpperInvariant()).ToList();
            index += 2;
        }

        if (index + 1 < tokens.Count && tokens[index].Text.Equals("CHARSET", StringComparison.OrdinalIgnoreCase))
            index += 2;

        string line;
        lock (Gate)
        {
            var folder = request.Session.Selected!;
            var criteria = new List<Func<FakeImapMessage, int, bool>>();
            while (index < tokens.Count)
                criteria.Add(ParseKey(folder, tokens, ref index));

            var hits = new List<uint>();
            for (var i = 0; i < folder.Messages.Count; i++)
            {
                var message = folder.Messages[i];
                if (criteria.TrueForAll(criterion => criterion(message, i + 1)))
                    hits.Add(request.Uid ? message.Uid : (uint)(i + 1));
            }

            line = returning is null
                ? "* SEARCH" + string.Concat(hits.Select(hit => " " + hit.ToString(CultureInfo.InvariantCulture)))
                : ExtendedSearchLine(request, hits, returning);
        }

        return await ReplyAsync(request, line, "OK SEARCH completed");
    }

    private static string ExtendedSearchLine(ImapRequest request, List<uint> hits, List<string> returning)
    {
        var line = new StringBuilder("* ESEARCH (TAG \"").Append(request.Tag).Append("\")");
        if (request.Uid)
            line.Append(" UID");
        if (hits.Count > 0)
        {
            if (returning.Count == 0 || returning.Contains("ALL"))
                line.Append(" ALL ").Append(SequenceSet(hits));
            if (returning.Contains("MIN"))
                line.Append(" MIN ").Append(hits.Min().ToString(CultureInfo.InvariantCulture));
            if (returning.Contains("MAX"))
                line.Append(" MAX ").Append(hits.Max().ToString(CultureInfo.InvariantCulture));
        }

        if (returning.Contains("COUNT"))
            line.Append(" COUNT ").Append(hits.Count.ToString(CultureInfo.InvariantCulture));
        return line.ToString();
    }

    /// <summary><c>1:3,5</c> for 1, 2, 3 and 5.</summary>
    private static string SequenceSet(List<uint> numbers)
    {
        var sorted = numbers.Order().ToList();
        var ranges = new List<string>();
        var start = sorted[0];
        var previous = start;
        foreach (var number in sorted.Skip(1).Append(uint.MaxValue))
        {
            if (number == previous + 1)
            {
                previous = number;
                continue;
            }

            ranges.Add(start == previous
                ? start.ToString(CultureInfo.InvariantCulture)
                : string.Create(CultureInfo.InvariantCulture, $"{start}:{previous}"));
            start = previous = number;
        }

        return string.Join(',', ranges);
    }

    private static Func<FakeImapMessage, int, bool> ParseKey(FakeImapFolder folder, List<Token> tokens, ref int index)
    {
        var token = tokens[index++];
        if (token.Kind == TokenKind.List)
        {
            var inner = new List<Func<FakeImapMessage, int, bool>>();
            var position = 0;
            while (position < token.Items.Count)
                inner.Add(ParseKey(folder, token.Items, ref position));
            return (message, sequence) => inner.TrueForAll(criterion => criterion(message, sequence));
        }

        var key = token.Text.ToUpperInvariant();
        switch (key)
        {
            case "ALL":
                return (_, _) => true;
            case "NOT":
            {
                var negated = ParseKey(folder, tokens, ref index);
                return (message, sequence) => !negated(message, sequence);
            }

            case "OR":
            {
                var left = ParseKey(folder, tokens, ref index);
                var right = ParseKey(folder, tokens, ref index);
                return (message, sequence) => left(message, sequence) || right(message, sequence);
            }

            case "SEEN" or "UNSEEN" or "FLAGGED" or "UNFLAGGED" or "DELETED" or "UNDELETED" or "DRAFT" or "UNDRAFT" or "ANSWERED" or "UNANSWERED":
            {
                var negative = key.StartsWith("UN", StringComparison.Ordinal);
                var flag = "\\" + (negative ? key[2..] : key);
                return (message, _) => message.Flags.Contains(flag) != negative;
            }

            case "RECENT" or "NEW":
                return (_, _) => false;
            case "OLD":
                return (_, _) => true;
            case "KEYWORD" or "UNKEYWORD":
            {
                var keyword = tokens[index++].Text;
                return (message, _) => message.Flags.Contains(keyword) == (key == "KEYWORD");
            }

            case "FROM" or "TO" or "CC" or "BCC" or "SUBJECT" or "BODY" or "TEXT":
            {
                var value = tokens[index++].Text;
                return (message, _) => Field(message.Facts, key).Contains(value, StringComparison.OrdinalIgnoreCase);
            }

            case "HEADER":
            {
                var field = tokens[index++].Text;
                var value = tokens[index++].Text;
                return (message, _) => message.Facts.Headers.Split('\n')
                    .Any(header => header.StartsWith(field + ":", StringComparison.OrdinalIgnoreCase) && header.Contains(value, StringComparison.OrdinalIgnoreCase));
            }

            case "SINCE" or "BEFORE" or "ON":
            {
                var date = DateTime.ParseExact(tokens[index++].Text, ["d-MMM-yyyy", "dd-MMM-yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None);
                return (message, _) =>
                {
                    var day = message.InternalDate.UtcDateTime.Date;
                    return key switch { "SINCE" => day >= date, "BEFORE" => day < date, _ => day == date };
                };
            }

            case "LARGER" or "SMALLER":
            {
                var size = int.Parse(tokens[index++].Text, CultureInfo.InvariantCulture);
                return (message, _) => key == "LARGER" ? message.Raw.Length > size : message.Raw.Length < size;
            }

            case "UID":
            {
                var set = ResolveSet(folder, tokens[index++].Text, uid: true);
                return (message, _) => set.Contains(message);
            }

            case "X-GM-RAW":
            {
                var query = tokens[index++].Text;
                return (message, _) => query.Equals("has:attachment", StringComparison.OrdinalIgnoreCase)
                    ? message.Facts.HasAttachments
                    : message.Facts.Subject.Contains(query, StringComparison.OrdinalIgnoreCase) || message.Facts.Body.Contains(query, StringComparison.OrdinalIgnoreCase);
            }

            default:
                if (key.Length > 0 && (char.IsAsciiDigit(key[0]) || key[0] == '*'))
                {
                    var set = ResolveSet(folder, key, uid: false);
                    return (message, _) => set.Contains(message);
                }

                throw new NotSupportedException($"The fake IMAP server cannot SEARCH '{key}'.");
        }
    }

    private static string Field(ImapMessageFacts facts, string key) => key switch
    {
        "FROM" => facts.From,
        "TO" => facts.To,
        "CC" => facts.Cc,
        "BCC" => facts.Bcc,
        "SUBJECT" => facts.Subject,
        "BODY" => facts.Body,
        _ => facts.Headers + "\n" + facts.Body,
    };
}
