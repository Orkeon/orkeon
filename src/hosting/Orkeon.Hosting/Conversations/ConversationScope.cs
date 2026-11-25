namespace Orkeon.Hosting.Conversations;

/// <summary>What became of one file an agent attached.</summary>
/// <param name="Path">The virtual path, as the agent wrote it.</param>
/// <param name="Status">One of <see cref="AttachedFileStatus"/>.</param>
/// <param name="Reason">Why, when it was not sent.</param>
internal sealed record AttachedFile(string Path, string Status, string? Reason = null);

/// <summary>What may become of a file an agent attached.</summary>
internal static class AttachedFileStatus
{
    /// <summary>It is in the conversation.</summary>
    public const string Sent = "sent";

    /// <summary>The channel takes no file of this size.</summary>
    public const string TooLarge = "too_large";

    /// <summary>It was not sent.</summary>
    public const string Refused = "refused";

    /// <summary>The person must accept it first.</summary>
    public const string AwaitingConsent = "awaiting_consent";
}

/// <summary>Sends files of a run to the conversation it answers, and to no other.</summary>
internal interface IConversationAttachments
{
    /// <summary>Whether the run is still in progress: what outlives it sends nothing.</summary>
    bool IsOpen { get; }

    /// <summary>Sends the files; the answer names each one, in their order.</summary>
    Task<IReadOnlyList<AttachedFile>> AttachAsync(IReadOnlyList<string> paths, CancellationToken ct);
}

/// <summary>
/// The conversation of the calling flow: entered by the host around a run, read at each call of a tool, which is a singleton
/// and outside the run's services. It flows to what the run starts, and to no other run.
/// </summary>
internal static class ConversationScope
{
    private static readonly AsyncLocal<IConversationAttachments?> s_current = new();

    /// <summary>The conversation of the calling flow; null outside one.</summary>
    public static IConversationAttachments? Current => s_current.Value;

    /// <summary>Makes a conversation, or the absence of one, that of the calling flow until the result is disposed.</summary>
    public static IDisposable Enter(IConversationAttachments? attachments)
    {
        var previous = s_current.Value;
        s_current.Value = attachments;
        return new Left(previous);
    }

    private sealed class Left(IConversationAttachments? previous) : IDisposable
    {
        public void Dispose() => s_current.Value = previous;
    }
}
