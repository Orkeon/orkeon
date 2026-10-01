using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Constants.Rag;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Options;
using Orkeon.Rag.Augmentation;

namespace Orkeon.Rag.DependencyInjection;

/// <summary>
/// Opt-in registration of the knowledge-context augmentation used at prompt
/// assembly time (RAG-03/C4). Kept separate from
/// <see cref="RagServiceCollectionExtensions.AddOrkeonRag"/> so hosts (and the
/// RAG bootstrap itself) can wire it explicitly; without this registration the
/// execution path is strictly unchanged (the orchestrator resolves the augmenter
/// with <c>GetService</c>).
/// </summary>
public static class KnowledgeAugmentationExtensions
{
    /// <summary>
    /// Registers the default <see cref="IKnowledgeContextAugmenter"/> over the ambient
    /// <see cref="IRagProfileResolver"/>: each attachment retrieves through the pipeline of
    /// its profile, and an attachment without one uses the <see cref="RagOptions.Profile"/>
    /// of the host (<c>Orkeon:Rag:Profile</c>, <c>fast</c> when unset).
    /// <c>TryAdd</c> semantics — a host-registered augmenter wins.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOrkeonKnowledgeAugmentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IKnowledgeContextAugmenter>(sp => new KnowledgeContextAugmenter(
            sp.GetRequiredService<IRagProfileResolver>(),
            sp.GetService<RagOptions>()?.Profile ?? RagDefaults.DefaultProfile,
            sp.GetService<ILogger<KnowledgeContextAugmenter>>()));

        return services;
    }
}
