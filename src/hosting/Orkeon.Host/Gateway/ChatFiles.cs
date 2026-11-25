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
