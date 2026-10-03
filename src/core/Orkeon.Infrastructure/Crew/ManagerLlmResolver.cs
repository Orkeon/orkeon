using Microsoft.Extensions.AI;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Adapters;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Infrastructure.Crew;

/// <summary>
/// Resolves the LLM a crew gives its manager (GAP-19): the provider C# sets with
/// <c>CrewBuilder.WithManagerLlm</c> — CrewAI's <c>manager_llm</c> —, metered for the host's
/// <see cref="ILlmUsageSink"/> like a provider the host registers; else the manager agent's own
/// <c>llm:</c> block, its profile (<see cref="ILlmProfileRegistry"/>) and model, a model left unset
/// being the profile's own; else the host's default profile. The hierarchical and autonomous
/// strategies resolve it once per run and hand it to every manager call.
/// </summary>
public sealed class ManagerLlmResolver
{
    private readonly IChatClient _defaultChatClient;
    private readonly ILlmProfileRegistry? _profiles;
    private readonly ILlmUsageSink? _usageSink;

    /// <summary>Builds the resolver over the host's language models.</summary>
    /// <param name="defaultChatClient">The host's default profile, as a chat client.</param>
    /// <param name="profiles">
    /// The host's named profiles; null — a strategy built by hand — offers the default alone, and a
    /// manager agent naming another profile fails with the list of known ones.
    /// </param>
    /// <param name="usageSink">Where the calls of a provider the crew sets are metered; null meters nothing.</param>
    public ManagerLlmResolver(
        IChatClient defaultChatClient,
        ILlmProfileRegistry? profiles = null,
        ILlmUsageSink? usageSink = null)
    {
        ArgumentNullException.ThrowIfNull(defaultChatClient);
        _defaultChatClient = defaultChatClient;
        _profiles = profiles;
        _usageSink = usageSink;
    }

    /// <summary>The LLM <paramref name="crew"/>'s manager runs on.</summary>
    /// <param name="crew">The crew whose manager it is.</param>
    /// <param name="managerAgent">The crew's manager agent; null when it has none.</param>
    /// <returns>The chat client, the model and the name of the manager's LLM.</returns>
    /// <exception cref="InvalidOperationException">
    /// The manager agent names a profile the host does not offer. The crew load checks it first;
    /// this is the guard for a crew built by hand.
    /// </exception>
    public ManagerLlm Resolve(DomainCrew crew, DomainAgent? managerAgent)
    {
        ArgumentNullException.ThrowIfNull(crew);
        var name = ManagerLlm.Describe(crew, managerAgent);

        // The provider the crew gave its manager wins, as CrewAI's manager_llm does over its
        // manager agent: metered here unless it already is, its own configuration the base.
        if (crew.ManagerLlm is { } provider)
        {
            return new ManagerLlm
            {
                ChatClient = new LlmProviderToChatClientAdapter(MeteredLlmProvider.Wrap(provider, _usageSink)),
                Name = name,
            };
        }

        var config = managerAgent?.LlmConfig;
        var model = string.IsNullOrWhiteSpace(config?.Model) ? null : config.Model;
        if (LlmProfiles.IsDefault(config?.Profile))
            return new ManagerLlm { ChatClient = _defaultChatClient, Model = model, Name = name };

        LlmProfiles.EnsureKnown(_profiles, config!.Profile, $"Manager agent '{managerAgent!.Role.Value}'");
        return new ManagerLlm
        {
            ChatClient = _profiles!.Resolve(config.Profile).ChatClient,
            Model = model,
            Name = name,
        };
    }
}
