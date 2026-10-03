using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Constants.Configuration;
using Orkeon.Rag.Abstractions.Options;

namespace Orkeon.Rag.DependencyInjection;

/// <summary>
/// The language model the RAG subsystem calls (GAP-19): the host's default profile — the
/// container's <see cref="IChatClient"/> —, or the host profile <c>Orkeon:Rag:LlmProfile</c> names
/// (<see cref="RagOptions.LlmProfile"/>, through <see cref="ILlmProfileRegistry"/>). Grounded
/// generation, the query transformers, the listwise reranker, the corrective graph's evaluator and
/// groundedness checker, the <c>llm</c> classifier and the evaluation judge all take their chat
/// client here, when they are first built — never at start-up, where only the name is checked
/// (<see cref="EnsureProfileIsKnown"/>).
/// </summary>
public static class RagLlm
{
    /// <summary>The configuration key naming the profile: <c>Orkeon:Rag:LlmProfile</c>.</summary>
    public const string ProfileKey = ConfigurationKeys.Rag + ":" + nameof(RagOptions.LlmProfile);

    /// <summary>
    /// Checks, by name alone — no provider is built —, that <c>Orkeon:Rag:LlmProfile</c> names a
    /// profile the host offers. A host calls it once its container is built, so that a typo refuses
    /// the start, listing the known profiles, rather than failing the first RAG query of a run; the
    /// runner host and the REPL do. Unset, blank or <c>default</c> always passes.
    /// </summary>
    /// <param name="configuration">The host configuration.</param>
    /// <param name="profiles">
    /// The host's profiles, a host allow-list applied (<c>Orkeon:Host:LlmProfiles</c>); null offers
    /// the default alone.
    /// </param>
    /// <exception cref="InvalidOperationException">The host offers no such profile; the message lists the known ones.</exception>
    public static void EnsureProfileIsKnown(IConfiguration configuration, ILlmProfileRegistry? profiles)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        LlmProfiles.EnsureKnown(profiles, configuration[ProfileKey], ProfileKey);
    }

    /// <summary>The subsystem's chat client; like <c>GetRequiredService</c>, it throws when the host has none.</summary>
    internal static IChatClient ChatClient(IServiceProvider services) =>
        Find(services) ?? services.GetRequiredService<IChatClient>();

    /// <summary>
    /// The subsystem's chat client, or null when the host registers none — the components with a
    /// deterministic fallback (heuristic classifier, evaluator, checker and judge) take it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The profile is unknown; the message lists the known ones.</exception>
    internal static IChatClient? Find(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var profile = services.GetService<RagOptions>()?.LlmProfile;
        if (LlmProfiles.IsDefault(profile))
            return services.GetService<IChatClient>();

        var registry = services.GetService<ILlmProfileRegistry>();
        LlmProfiles.EnsureKnown(registry, profile, ProfileKey);
        return registry!.Resolve(profile).ChatClient;
    }
}
