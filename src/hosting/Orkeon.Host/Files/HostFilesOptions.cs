namespace Orkeon.Host.Files;

/// <summary>
/// The files of the chat conversations, bound from <c>Orkeon:Host:Files</c>. Off by default: a daemon keeps no file
/// nobody asked it to keep.
/// </summary>
internal sealed record HostFilesOptions
{
    /// <summary>Configuration section this binds to.</summary>
    public const string SectionName = OrkeonHostOptions.SectionName + ":Files";

    /// <summary>
    /// Gives each conversation two folders, mounted for its runs: <c>/inbox</c>, read-only, and <c>/outbox</c>. Off, no
    /// folder is made and neither path is mounted.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// The folder the conversations are kept in, relative to the service's working directory. No mount and no crew folder
    /// may hold it, be it or be under it, links followed: the start is refused.
    /// </summary>
    public string Root { get; init; } = "conversations";

    /// <summary>The largest file taken, in bytes.</summary>
    public long MaxFileBytes { get; init; } = 10L * 1024 * 1024;

    /// <summary>How many files of one message are taken.</summary>
    public int MaxFilesPerMessage { get; init; } = 10;

    /// <summary>
    /// What one conversation may hold, in bytes, both folders counted. A run writes freely while it runs: a conversation
    /// found over this afterwards starts no other run.
    /// </summary>
    public long MaxConversationBytes { get; init; } = 100L * 1024 * 1024;

    /// <summary>
    /// What the conversations may hold together, in bytes. Found over this, no conversation starts a run until the
    /// retention or the operator makes room.
    /// </summary>
    public long MaxTotalBytes { get; init; } = 2L * 1024 * 1024 * 1024;

    /// <summary>
    /// How long a conversation is kept after its last activity. One with a run in progress, or suspended on a question,
    /// is kept.
    /// </summary>
    public TimeSpan Retention { get; init; } = TimeSpan.FromDays(7);

    /// <summary>The extensions taken (<c>pdf</c> or <c>.pdf</c>, case ignored). Empty, every extension is.</summary>
    public IReadOnlyList<string> AllowedExtensions { get; init; } = [];

    /// <summary>The first value the host cannot run on, as a sentence naming its key; null when there is none.</summary>
    internal string? Problem()
    {
        if (string.IsNullOrWhiteSpace(Root))
            return $"{SectionName}:Root is empty.";
        if (MaxFileBytes <= 0)
            return $"{SectionName}:MaxFileBytes must be positive.";
        if (MaxFilesPerMessage <= 0)
            return $"{SectionName}:MaxFilesPerMessage must be positive.";
        if (MaxConversationBytes <= 0)
            return $"{SectionName}:MaxConversationBytes must be positive.";
        if (MaxTotalBytes <= 0)
            return $"{SectionName}:MaxTotalBytes must be positive.";
        if (Retention <= TimeSpan.Zero)
            return $"{SectionName}:Retention must be positive.";
        return AllowedExtensions.Any(e => string.IsNullOrWhiteSpace(e?.TrimStart('.')))
            ? $"{SectionName}:AllowedExtensions holds an empty entry."
            : null;
    }

    /// <summary>Whether a file of this name is taken, by its extension.</summary>
    internal bool Takes(string name) =>
        AllowedExtensions.Count == 0
        || AllowedExtensions.Any(e => string.Equals(e.Trim().TrimStart('.'), Path.GetExtension(name).TrimStart('.'), StringComparison.OrdinalIgnoreCase));
}
