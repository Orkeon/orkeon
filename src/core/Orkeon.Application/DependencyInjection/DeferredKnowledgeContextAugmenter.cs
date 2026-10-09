using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.Knowledge;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Application.DependencyInjection;

/// <summary>
/// The registered <see cref="IKnowledgeContextAugmenter"/>, resolved at its first use rather
/// than when the execution orchestrator is built (GAP-02). The orchestrator only calls it for
/// an agent that carries a knowledge attachment, so a crew without <c>knowledge:</c> never
/// resolves the RAG subsystem — and an unusable RAG configuration (an unknown
/// <c>Orkeon:Rag:Provider</c> or profile) fails the agent that needs knowledge, not every crew.
/// </summary>
internal sealed class DeferredKnowledgeContextAugmenter : IKnowledgeContextAugmenter
{
    private readonly Lazy<IKnowledgeContextAugmenter> _inner;

    public DeferredKnowledgeContextAugmenter(Func<IKnowledgeContextAugmenter> resolve)
    {
        _inner = new Lazy<IKnowledgeContextAugmenter>(resolve);
    }

    /// <summary>
    /// A deferred view of the provider's augmenter, or <c>null</c> when none is registered
    /// (the host has no RAG subsystem).
    /// </summary>
    public static IKnowledgeContextAugmenter? For(IServiceProvider services)
    {
        var registered = services.GetService<IServiceProviderIsService>()
            ?.IsService(typeof(IKnowledgeContextAugmenter));

        return registered switch
        {
            true => new DeferredKnowledgeContextAugmenter(services.GetRequiredService<IKnowledgeContextAugmenter>),
            false => null,
            // A provider that cannot answer the question: resolve now, as before.
            null => services.GetService<IKnowledgeContextAugmenter>(),
        };
    }

    public Task<KnowledgeContextBlock?> BuildContextAsync(
        IReadOnlyList<KnowledgeAttachment> attachments,
        string taskInput,
        CancellationToken cancellationToken = default)
        => _inner.Value.BuildContextAsync(attachments, taskInput, cancellationToken);
}
