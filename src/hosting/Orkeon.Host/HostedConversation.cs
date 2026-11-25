using System.Security.Cryptography;
using System.Text;

namespace Orkeon.Host;

/// <summary>The conversation a run belongs to. A run asked over the host API or A2A has none.</summary>
internal sealed record HostedConversation
{
    /// <summary>The channel it is on.</summary>
    public required string Channel { get; init; }

    /// <summary>The channel's identifier of it: on Teams it holds characters no folder name takes.</summary>
    public required string ConversationId { get; init; }

    /// <summary>What names the conversation where a name is restricted: a hash of its channel and identifier.</summary>
    public string Key => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{Channel}\n{ConversationId}")));

    /// <summary>The conversation's folders, null when it has none.</summary>
    public ConversationFolders? Folders { get; init; }

    /// <summary>
    /// Prepares the run, once it is admitted and acknowledged, under the run's own token and deadline. What it returns
    /// replaces the run's prompt and variables.
    /// </summary>
    public required Func<CancellationToken, Task<PreparedRun>> PrepareAsync { get; init; }
}

/// <summary>The physical folders of a conversation.</summary>
/// <param name="Inbox">What the conversation was given.</param>
/// <param name="Outbox">What its runs hand back.</param>
internal sealed record ConversationFolders(string Inbox, string Outbox);

/// <summary>What a run starts on, once its conversation is prepared.</summary>
/// <param name="Prompt">The prompt.</param>
/// <param name="Variables">The input variables.</param>
internal sealed record PreparedRun(string Prompt, IReadOnlyDictionary<string, string>? Variables = null)
{
    /// <summary>The files that were not taken.</summary>
    public IReadOnlyList<RefusedFile> RefusedFiles { get; init; } = [];
}

/// <summary>A file that was not taken.</summary>
/// <param name="Name">Its name, made safe.</param>
/// <param name="Reason">Why, in words for a person.</param>
internal sealed record RefusedFile(string Name, string Reason);

/// <summary>A file of the conversation's outbox.</summary>
/// <param name="Path">Its virtual path.</param>
/// <param name="Size">Its size in bytes.</param>
internal sealed record OutboxFile(string Path, long Size);

/// <summary>A preparation that failed for a reason the person is told: the message is shown as it is.</summary>
internal sealed class ConversationPreparationException : Exception
{
    /// <summary>Builds the failure without a reason to show.</summary>
    public ConversationPreparationException()
        : this("This message could not be prepared.")
    {
    }

    /// <summary>Builds the failure from the words shown to the person.</summary>
    public ConversationPreparationException(string message)
        : base(message)
    {
    }

    /// <summary>Builds the failure from the words shown to the person and its cause, which only the log keeps.</summary>
    public ConversationPreparationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
