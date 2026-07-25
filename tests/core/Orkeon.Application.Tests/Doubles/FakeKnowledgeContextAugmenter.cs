using Orkeon.Domain.Knowledge;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>
/// Hand-written double for <see cref="IKnowledgeContextAugmenter"/> (RAG-03/C4):
/// returns <see cref="BlockToReturn"/> and records every call for inspection.
/// </summary>
public sealed class FakeKnowledgeContextAugmenter : IKnowledgeContextAugmenter
{
    /// <summary>Block returned by every call (null by default).</summary>
    public KnowledgeContextBlock? BlockToReturn { get; set; }

    /// <summary>Calls received, in order.</summary>
    public List<(IReadOnlyList<KnowledgeAttachment> Attachments, string TaskInput)> Calls { get; } = [];

    public System.Threading.Tasks.Task<KnowledgeContextBlock?> BuildContextAsync(
        IReadOnlyList<KnowledgeAttachment> attachments,
        string taskInput,
        CancellationToken cancellationToken = default)
    {
        Calls.Add((attachments, taskInput));
        return System.Threading.Tasks.Task.FromResult(BlockToReturn);
    }
}
