namespace Orkeon.Host.Gateway;

/// <summary>
/// A file of an incoming message, as the channel announces it. It never holds the file's address: those are signed,
/// expire, and belong in no log.
/// </summary>
internal sealed record InboundAttachment
{
    /// <summary>The channel's own identifier of the file: the same file delivered twice has the same one.</summary>
    public required string Id { get; init; }

    /// <summary>The name the sender gave it. Untrusted: sanitize it before showing or writing it.</summary>
    public required string Name { get; init; }

    /// <summary>The announced media type, when there is one.</summary>
    public string? ContentType { get; init; }

    /// <summary>The announced size in bytes, when there is one. A claim: the bytes read are what counts.</summary>
    public long? Size { get; init; }
}

/// <summary>A file was not read, for a reason a person may be told: the message holds no address and nothing a server answered.</summary>
internal sealed class AttachmentRefusedException : IOException
{
    public AttachmentRefusedException()
    {
    }

    /// <param name="reason">The reason, as the end of a sentence.</param>
    public AttachmentRefusedException(string reason)
        : base(reason)
    {
    }

    public AttachmentRefusedException(string reason, Exception innerException)
        : base(reason, innerException)
    {
    }
}

/// <summary>Reads the files of one message or command, on the channel they came from. It holds their addresses.</summary>
internal interface IAttachmentSource
{
    /// <summary>
    /// Opens a file for reading; the caller disposes the stream. Throws when the file cannot be read: that file is then
    /// refused, not the message.
    /// </summary>
    Task<Stream> OpenAsync(InboundAttachment attachment, CancellationToken ct);
}

/// <summary>A file to send to a conversation.</summary>
internal sealed record OutboundFile
{
    /// <summary>The name it is sent under.</summary>
    public required string Name { get; init; }

    /// <summary>Its size in bytes: channels bound each file and each request.</summary>
    public required long Size { get; init; }

    /// <summary>When it was last written, as it was found; with the size, it tells whether it changed since.</summary>
    public DateTimeOffset? Written { get; init; }

    /// <summary>Opens its content. Called only when the file is about to leave; the channel disposes the stream.</summary>
    public required Func<CancellationToken, Task<Stream>> OpenAsync { get; init; }
}

/// <summary>What became of a file that was to be sent.</summary>
internal enum FileSendOutcome
{
    /// <summary>It is in the conversation.</summary>
    Sent,

    /// <summary>The channel takes no file of this size.</summary>
    TooLarge,

    /// <summary>It was not sent, for the reason given.</summary>
    Refused,

    /// <summary>The person must accept it first (Teams).</summary>
    AwaitingConsent,
}

/// <summary>What became of one file that was to be sent.</summary>
/// <param name="Name">The file's name.</param>
/// <param name="Outcome">What became of it.</param>
/// <param name="Reason">Why, in words for a person, when it was not sent.</param>
internal sealed record FileSendResult(string Name, FileSendOutcome Outcome, string? Reason = null)
{
    /// <summary>The answer of a channel that sends no file: each one refused, none opened.</summary>
    internal static Task<IReadOnlyList<FileSendResult>> NoneSent(IReadOnlyList<OutboundFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        return Task.FromResult<IReadOnlyList<FileSendResult>>(
            [.. files.Select(f => new FileSendResult(f.Name, FileSendOutcome.Refused, "This channel does not send files."))]);
    }
}

/// <summary>The words about files that were not taken.</summary>
internal static class FileNotices
{
    private const int MaxNames = 10;
    private const int MaxShown = 64;
    private const int MaxReason = 120;

    /// <summary>One sentence naming the files of a message, none of which was taken.</summary>
    public static string NotTaken(IReadOnlyList<InboundAttachment> attachments)
    {
        ArgumentNullException.ThrowIfNull(attachments);

        var names = attachments.Take(MaxNames).Select((a, i) => Quoted(a.Name, i));
        return $"Files are not taken here, so these were left out: {string.Join(", ", names)}{More(attachments.Count)}.";
    }

    /// <summary>One sentence naming the files that were refused, each with its reason.</summary>
    public static string Refused(IReadOnlyList<RefusedFile> refused)
    {
        ArgumentNullException.ThrowIfNull(refused);
        var names = refused.Take(MaxNames).Select((f, i) => $"{Shown(f.Name, i)} ({f.Reason})");
        return $"These files were not taken: {string.Join(", ", names)}{More(refused.Count)}.";
    }

    /// <summary>One sentence naming the files that were not sent, each with its reason.</summary>
    public static string NotSent(IReadOnlyList<FileSendResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var names = results.Take(MaxNames).Select((r, i) => $"{Shown(r.Name, i)} ({r.Reason})");
        return $"These files were not sent: {string.Join(", ", names)}{More(results.Count)}.";
    }

    /// <summary>A virtual path, as code: what an agent named its file renders nothing and mentions nobody.</summary>
    public static string Path(string path) => $"`{Inline(path, int.MaxValue)}`";

    /// <summary>A virtual path and the size of the file.</summary>
    public static string PathAndSize(string path, long size) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Path(path)} ({size} bytes)");

    /// <summary>An image an agent was not shown, and why.</summary>
    public static string NotShown(Orkeon.Application.Interfaces.Services.UnseenImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return $"{Path(image.Path)} to `{Inline(image.Reader, MaxShown)}` ({Inline(image.Reason, MaxReason)})";
    }

    /// <summary>One line of plain text, cut at <paramref name="limit"/>.</summary>
    public static string Inline(string text, int limit)
    {
        ArgumentNullException.ThrowIfNull(text);
        var line = string.Concat(text.EnumerateRunes().Select(r => IsPlain(r) ? r.Value == '`' ? "'" : r.ToString() : "_"));
        if (line.Length <= limit)
            return line;
        var end = char.IsHighSurrogate(line[limit - 2]) ? limit - 2 : limit - 1;
        return line[..end] + "\u2026";
    }

    /// <summary>Whether the text shows as it is: no control, no invisible or direction mark, no line break.</summary>
    public static bool IsPlain(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.EnumerateRunes().All(IsPlain);
    }

    private static bool IsPlain(System.Text.Rune rune) => System.Text.Rune.GetUnicodeCategory(rune)
        is not (System.Globalization.UnicodeCategory.Control
            or System.Globalization.UnicodeCategory.Format
            or System.Globalization.UnicodeCategory.Surrogate
            or System.Globalization.UnicodeCategory.PrivateUse
            or System.Globalization.UnicodeCategory.LineSeparator
            or System.Globalization.UnicodeCategory.ParagraphSeparator)
        && rune != System.Text.Rune.ReplacementChar;

    /// <summary>As many of the items as the limit holds, and how many are left.</summary>
    public static string Bounded(IReadOnlyList<string> items, string separator, int limit)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(separator);
        var text = new System.Text.StringBuilder();
        var shown = 0;
        foreach (var item in items)
        {
            if (text.Length + separator.Length + item.Length > limit)
                break;
            if (shown++ > 0)
                text.Append(separator);
            text.Append(item);
        }

        if (shown == items.Count)
            return text.ToString();
        if (shown > 0)
            text.Append(separator);
        return text.Append(System.Globalization.CultureInfo.InvariantCulture, $"and {items.Count - shown} more").ToString();
    }

    private static string More(int count) => count > MaxNames ? $" and {count - MaxNames} more" : string.Empty;

    // Quoted as code, so neither Markdown nor a mention in a name is rendered; the names are the sender's.
    private static string Quoted(string name, int index) =>
        $"`{Orkeon.Tools.Email.Mime.SafeFileNames.Sanitize(name, index).Replace('`', '\'')}`";

    // Short enough for the notice of ten files to fit a chat message.
    private static string Shown(string name, int index)
    {
        var quoted = Quoted(name, index);
        if (quoted.Length <= MaxShown)
            return quoted;

        var end = char.IsHighSurrogate(quoted[MaxShown - 3]) ? MaxShown - 3 : MaxShown - 2;
        return $"{quoted[..end]}\u2026`";
    }
}
