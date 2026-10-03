using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.Agent;

/// <summary>
/// The rules of an agent's own provider (<see cref="Agent.Llm"/>, GAP-34), worded once for the
/// aggregate, its builder and the run that resolves what a task runs on. An agent runs on its own
/// provider or on one of the host's profiles, never both; and a provider that runs its own tools
/// (<see cref="LlmProviderCapabilities.RunsOwnTools"/>) is never handed Orkeon's: they would be listed
/// in a prompt it cannot act on, and a tool call it wrote would come back as its answer.
/// </summary>
public static class AgentLlmRules
{
    /// <summary>
    /// The tools delegation gives an agent (<see cref="Agent.AllowDelegation"/>), as the
    /// infrastructure names them.
    /// </summary>
    internal static IReadOnlyList<string> DelegationTools { get; } = ["delegate_work_to_coworker", "ask_question_to_coworker"];

    /// <summary>
    /// The refusal of Orkeon tools for <paramref name="holder"/>, which answers through
    /// <paramref name="providerName"/>, a provider that runs its own tools: it names the tools, the
    /// provider and the two remedies — the tool on the agent behind the provider, or that agent as a
    /// tool of an Orkeon agent —, and, when delegation brought some of them, how to switch it off.
    /// </summary>
    /// <param name="holder">Who would hold the tools — <c>Agent 'Reviewer'</c>, <c>Task 'Review the change'</c>.</param>
    /// <param name="providerName">The provider's name (<c>agent-framework:Reviewer</c>).</param>
    /// <param name="toolNames">The tools refused.</param>
    /// <returns>The operator-facing message.</returns>
    public static string OwnToolsRefusal(string holder, string providerName, IEnumerable<string> toolNames)
    {
        ArgumentNullException.ThrowIfNull(toolNames);
        var names = toolNames.ToList();
        var message = $"{holder} holds Orkeon tools ({string.Join(", ", names)}) but answers through {providerName}, " +
            "which runs its own tools: an Orkeon tool would be listed in its prompt and never called. Give the tool " +
            $"to the agent behind {providerName} — a Microsoft Agent Framework agent calls the tools it carries —, or " +
            "give that agent to an Orkeon agent as a tool (WithAgentFrameworkTool) rather than as its model.";
        return names.Exists(name => DelegationTools.Contains(name, StringComparer.OrdinalIgnoreCase))
            ? message + " Delegation gives an agent delegate_work_to_coworker and ask_question_to_coworker: switch it " +
                "off for this agent (allowDelegation: false in YAML, AllowDelegation(false) in C#)."
            : message;
    }

    /// <summary>
    /// Whether <paramref name="profile"/> leaves the agent on no host profile of its own: null, blank
    /// or the default profile's reserved name (<c>default</c>).
    /// </summary>
    internal static bool NamesNoProfile(string? profile) =>
        string.IsNullOrWhiteSpace(profile)
        || string.Equals(profile.Trim(), Orkeon.Constants.Llm.LlmProfileNames.Default, StringComparison.OrdinalIgnoreCase);
}
