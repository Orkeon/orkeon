using Microsoft.Extensions.AI;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel.ValueObjects;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Application.Interfaces;

/// <summary>
/// The language model a crew gives its manager, as a run resolved it (GAP-19): the provider the
/// crew sets in C# (<c>CrewBuilder.WithManagerLlm</c>, CrewAI's <c>manager_llm</c>), else its manager
/// agent's <c>llm:</c> block — profile and model —, else the host's default profile. The hierarchical
/// and autonomous strategies resolve it once per run and hand it to every <see cref="IManagerAgent"/>
/// call: the manager never falls back on the default in silence.
/// </summary>
public sealed class ManagerLlm
{
    /// <summary>Prefix of <see cref="Name"/> for a provider the crew sets in C#.</summary>
    public const string ProviderPrefix = "provider:";

    /// <summary>Prefix of <see cref="Name"/> for one of the host's profiles.</summary>
    public const string ProfilePrefix = "profile:";

    /// <summary>The chat client every call of the manager goes to.</summary>
    public required IChatClient ChatClient { get; init; }

    /// <summary>
    /// The model each call asks for — the manager agent's own; null names none, and the provider
    /// runs the call on its own model, the profile's (GAP-18).
    /// </summary>
    public string? Model { get; init; }

    /// <summary>What the manager runs on, as the log and the crew's metadata name it (<see cref="Describe"/>).</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Names what <paramref name="crew"/>'s manager runs on, resolving nothing:
    /// <c>provider:&lt;name&gt;</c> for the provider the crew sets, else <c>profile:&lt;name&gt;</c> for
    /// its manager agent's profile — <c>profile:default</c> for the host's default.
    /// </summary>
    /// <param name="crew">The crew whose manager it is.</param>
    /// <param name="managerAgent">The crew's manager agent; null when it has none.</param>
    /// <returns>The manager's LLM, by name.</returns>
    public static string Describe(DomainCrew crew, DomainAgent? managerAgent)
    {
        ArgumentNullException.ThrowIfNull(crew);
        if (crew.ManagerLlm is { } provider)
            return ProviderPrefix + provider.Name;

        return ProfilePrefix + ProfileOf(managerAgent?.LlmConfig);
    }

    /// <summary>The profile a manager agent's configuration names; <see cref="LlmProfiles.Default"/> when it names none.</summary>
    private static string ProfileOf(LlmConfig? config) =>
        LlmProfiles.IsDefault(config?.Profile) ? LlmProfiles.Default : config!.Profile!.Trim();
}
