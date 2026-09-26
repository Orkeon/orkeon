using System.Globalization;
using System.Text;
using MimeKit;

namespace Orkeon.Tools.Email.Tests.Doubles;

/// <summary>What the fake IMAP server derives once from a message's bytes.</summary>
internal sealed record ImapMessageFacts(
    string Envelope,
    string BodyStructure,
    string? Preview,
    string From,
    string To,
    string Cc,
    string Bcc,
    string Subject,
    string Body,
    string Headers,
    int HeaderLength,
    bool HasAttachments);

/// <summary>FETCH of the fake: items, ENVELOPE and BODYSTRUCTURE generated from the MIME.</summary>
internal sealed partial class FakeImapServer
{
    private static readonly string[] AllMacro = ["FLAGS", "INTERNALDATE", "RFC822.SIZE", "ENVELOPE"];
    private static readonly string[] FastMacro = ["FLAGS", "INTERNALDATE", "RFC822.SIZE"];
    private static readonly string[] FullMacro = ["FLAGS", "INTERNALDATE", "RFC822.SIZE", "ENVELOPE", "BODY"];

    /// <summary>Parses <paramref name="raw"/> once for everything FETCH and SEARCH need.</summary>
    internal static ImapMessageFacts Inspect(byte[] raw)
    {
        using var stream = new MemoryStream(raw, writable: false);
        using var message = MimeMessage.Load(stream);
        var separator = IndexOf(raw, "\r\n\r\n"u8);
        var body = message.TextBody ?? message.HtmlBody ?? string.Empty;
        var preview = string.Join(' ', body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return new ImapMessageFacts(
            Envelope(message),
            Structure(message.Body),
            preview.Length == 0 ? null : preview[..Math.Min(preview.Length, 200)],
            message.From.ToString(),
            message.To.ToString(),
            message.Cc.ToString(),
            message.Bcc.ToString(),
            message.Subject ?? string.Empty,
            body,
            string.Join("\n", message.Headers.Select(header => $"{header.Field}: {header.Value}")),
            separator < 0 ? raw.Length : separator + 4,
            message.Attachments.Any());
    }

    private async Task<bool> FetchAsync(ImapRequest request)
    {
        if (request.Arguments.Count < 2)
            return await ReplyAsync(request, null, "BAD FETCH needs a set and items");

        var items = FetchItems(request.Arguments[1]);
        byte[] payload;
        lock (Gate)
        {
            var folder = request.Session.Selected!;
            using var output = new MemoryStream();
            foreach (var message in ResolveSet(folder, request.Arguments[0].Text, request.Uid))
                WriteFetch(output, folder, message, items, request.Uid, request.Session.ReadOnly);
            payload = output.ToArray();
        }

        await request.Session.Connection.WriteAsync(payload, request.CancellationToken);
        await TaggedAsync(request, "OK FETCH completed");
        return true;
    }

    private static List<string> FetchItems(Token token)
    {
        var names = token.Kind == TokenKind.List
            ? token.Items.Where(item => item.Kind != TokenKind.List).Select(item => item.Text).ToList()
            : [token.Text];
        var items = new List<string>();
        foreach (var name in names)
        {
            var upper = name.ToUpperInvariant();
            items.AddRange(upper switch
            {
                "ALL" => AllMacro,
                "FAST" => FastMacro,
                "FULL" => FullMacro,
                _ => [name],
            });
        }

        return items;
    }

    private static void WriteFetch(MemoryStream output, FakeImapFolder folder, FakeImapMessage message, List<string> items, bool uid, bool readOnly)
    {
        var parts = new List<object>();
        if (uid || items.Exists(item => item.Equals("UID", StringComparison.OrdinalIgnoreCase)))
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"UID {message.Uid}"));

        var marksSeen = false;
        foreach (var item in items)
        {
            var upper = item.ToUpperInvariant();
            switch (upper)
            {
                case "UID":
                    break;
                case "FLAGS":
                    parts.Add($"FLAGS ({string.Join(' ', message.Flags)})");
                    break;
                case "INTERNALDATE":
                    parts.Add($"INTERNALDATE \"{message.InternalDate.ToUniversalTime().ToString("dd-MMM-yyyy HH:mm:ss", CultureInfo.InvariantCulture)} +0000\"");
                    break;
                case "RFC822.SIZE":
                    parts.Add(string.Create(CultureInfo.InvariantCulture, $"RFC822.SIZE {message.Raw.Length}"));
                    break;
                case "ENVELOPE":
                    parts.Add("ENVELOPE " + message.Facts.Envelope);
                    break;
                case "BODYSTRUCTURE" or "BODY":
                    parts.Add(upper + " " + message.Facts.BodyStructure);
                    break;
                case "PREVIEW":
                    parts.Add("PREVIEW " + NString(message.Facts.Preview));
                    break;
                case "RFC822":
                    parts.Add(new LiteralPart("RFC822", message.Raw));
                    marksSeen = true;
                    break;
                case "RFC822.HEADER":
                    parts.Add(new LiteralPart("RFC822.HEADER", message.Raw[..message.Facts.HeaderLength]));
                    break;
                case "RFC822.TEXT":
                    parts.Add(new LiteralPart("RFC822.TEXT", message.Raw[message.Facts.HeaderLength..]));
                    marksSeen = true;
                    break;
                default:
                    if (!upper.StartsWith("BODY[", StringComparison.Ordinal) && !upper.StartsWith("BODY.PEEK[", StringComparison.Ordinal))
                        throw new NotSupportedException($"The fake IMAP server cannot FETCH '{item}'.");
                    parts.Add(Section(message, upper));
                    marksSeen |= !upper.StartsWith("BODY.PEEK[", StringComparison.Ordinal);
                    break;
            }
        }

        if (marksSeen && !readOnly && message.Flags.Add("\\Seen") && !items.Exists(item => item.Equals("FLAGS", StringComparison.OrdinalIgnoreCase)))
            parts.Add($"FLAGS ({string.Join(' ', message.Flags)})");

        Write(output, string.Create(CultureInfo.InvariantCulture, $"* {folder.Messages.IndexOf(message) + 1} FETCH ("));
        for (var i = 0; i < parts.Count; i++)
        {
            if (i > 0)
                Write(output, " ");
            if (parts[i] is LiteralPart literal)
            {
                Write(output, string.Create(CultureInfo.InvariantCulture, $"{literal.Name} {{{literal.Bytes.Length}}}\r\n"));
                output.Write(literal.Bytes);
            }
            else
            {
                Write(output, (string)parts[i]);
            }
        }

        Write(output, ")\r\n");
    }

    /// <summary>A <c>BODY[section]&lt;partial&gt;</c> item as its response name and bytes.</summary>
    private static LiteralPart Section(FakeImapMessage message, string item)
    {
        var open = item.IndexOf('[', StringComparison.Ordinal);
        var close = item.IndexOf(']', StringComparison.Ordinal);
        var section = item[(open + 1)..close];
        var bytes = section switch
        {
            "" => message.Raw,
            "HEADER" => message.Raw[..message.Facts.HeaderLength],
            "TEXT" => message.Raw[message.Facts.HeaderLength..],
            _ when section.StartsWith("HEADER.FIELDS", StringComparison.Ordinal) => message.Raw[..message.Facts.HeaderLength],
            _ => throw new NotSupportedException($"The fake IMAP server cannot FETCH the section '{section}'."),
        };

        var name = $"BODY[{section}]";
        var partial = item[(close + 1)..];
        if (partial.StartsWith('<'))
        {
            var range = partial.Trim('<', '>').Split('.');
            var start = Math.Min(int.Parse(range[0], CultureInfo.InvariantCulture), bytes.Length);
            var length = range.Length > 1 ? Math.Min(int.Parse(range[1], CultureInfo.InvariantCulture), bytes.Length - start) : bytes.Length - start;
            bytes = bytes[start..(start + length)];
            name += string.Create(CultureInfo.InvariantCulture, $"<{start}>");
        }

        return new LiteralPart(name, bytes);
    }

    private static string Envelope(MimeMessage message)
    {
        var from = Addresses(message.From);
        var sender = message.Sender is { } single ? Addresses(new InternetAddressList { single }) : from;
        var replyTo = message.ReplyTo.Count > 0 ? Addresses(message.ReplyTo) : from;
        var date = message.Headers[HeaderId.Date];
        var inReplyTo = message.Headers[HeaderId.InReplyTo];
        var messageId = message.MessageId is { } id ? $"<{id}>" : null;
        return $"({NString(date)} {NString(message.Subject)} {from} {sender} {replyTo} {Addresses(message.To)} {Addresses(message.Cc)} {Addresses(message.Bcc)} {NString(inReplyTo)} {NString(messageId)})";
    }

    private static string Addresses(InternetAddressList list)
    {
        var mailboxes = list.Mailboxes.ToList();
        if (mailboxes.Count == 0)
            return "NIL";

        return "(" + string.Concat(mailboxes.Select(mailbox =>
            $"({NString(string.IsNullOrEmpty(mailbox.Name) ? null : mailbox.Name)} NIL {Quote(mailbox.LocalPart)} {Quote(mailbox.Domain)})")) + ")";
    }

    private static string Structure(MimeEntity? body) =>
        body is null ? "(\"TEXT\" \"PLAIN\" NIL NIL NIL \"7BIT\" 0 0 NIL NIL NIL NIL)" : BodyStructure(body);

    private static string BodyStructure(MimeEntity entity) => entity switch
    {
        Multipart multipart =>
            "(" + string.Concat(multipart.Select(BodyStructure)) + " " + Quote(multipart.ContentType.MediaSubtype.ToUpperInvariant()) + " "
            + Parameters(multipart.ContentType.Parameters) + " " + Disposition(multipart.ContentDisposition) + " NIL NIL)",
        MessagePart part => MessageStructure(part),
        MimePart part => PartStructure(part),
        _ => throw new NotSupportedException($"The fake IMAP server cannot describe {entity.GetType().Name}."),
    };

    private static string MessageStructure(MessagePart part)
    {
        var inner = part.Message ?? throw new NotSupportedException("An empty message/rfc822 part.");
        using var buffer = new MemoryStream();
        inner.WriteTo(buffer);
        var bytes = buffer.ToArray();
        return string.Create(CultureInfo.InvariantCulture,
            $"(\"MESSAGE\" \"RFC822\" {Parameters(part.ContentType.Parameters)} NIL NIL \"7BIT\" {bytes.Length} {Envelope(inner)} {Structure(inner.Body)} {Lines(bytes)} NIL {Disposition(part.ContentDisposition)} NIL NIL)");
    }

    private static string PartStructure(MimePart part)
    {
        var bytes = EncodedContent(part);
        var type = part.ContentType;
        var text = type.MediaType.Equals("text", StringComparison.OrdinalIgnoreCase);
        var structure = new StringBuilder("(")
            .Append(Quote(type.MediaType.ToUpperInvariant())).Append(' ')
            .Append(Quote(type.MediaSubtype.ToUpperInvariant())).Append(' ')
            .Append(Parameters(type.Parameters)).Append(' ')
            .Append(NString(part.ContentId is { } id ? $"<{id}>" : null)).Append(" NIL ")
            .Append(Quote(EncodingName(part.ContentTransferEncoding))).Append(' ')
            .Append(bytes.Length.ToString(CultureInfo.InvariantCulture));
        if (text)
            structure.Append(' ').Append(Lines(bytes).ToString(CultureInfo.InvariantCulture));
        return structure.Append(" NIL ").Append(Disposition(part.ContentDisposition)).Append(" NIL NIL)").ToString();
    }

    private static byte[] EncodedContent(MimePart part)
    {
        if (part.Content?.Stream is not { } stream)
            return [];
        using var copy = new MemoryStream();
        stream.Position = 0;
        stream.CopyTo(copy);
        stream.Position = 0;
        return copy.ToArray();
    }

    private static string Parameters(ParameterList parameters) =>
        parameters.Count == 0
            ? "NIL"
            : "(" + string.Join(' ', parameters.Select(p => $"{Quote(p.Name.ToUpperInvariant())} {Quote(p.Value)}")) + ")";

    private static string Disposition(ContentDisposition? disposition) =>
        disposition is null
            ? "NIL"
            : $"({Quote(disposition.Disposition.ToUpperInvariant())} {Parameters(disposition.Parameters)})";

    private static string EncodingName(ContentEncoding encoding) => encoding switch
    {
        ContentEncoding.Base64 => "BASE64",
        ContentEncoding.QuotedPrintable => "QUOTED-PRINTABLE",
        ContentEncoding.EightBit => "8BIT",
        ContentEncoding.Binary => "BINARY",
        _ => "7BIT",
    };

    private static int Lines(byte[] bytes) => bytes.Count(b => b == (byte)'\n');

    private static int IndexOf(byte[] haystack, ReadOnlySpan<byte> needle) => haystack.AsSpan().IndexOf(needle);

    private static void Write(MemoryStream output, string text) => output.Write(Encoding.UTF8.GetBytes(text));

    /// <summary>A FETCH item answered with a literal.</summary>
    private sealed record LiteralPart(string Name, byte[] Bytes);
}
