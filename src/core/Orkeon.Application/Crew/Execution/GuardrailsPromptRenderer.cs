using System.Text;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Constants.Agent;
using Orkeon.Domain.Task;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// Shared rendering of <see cref="GuardrailsConfig"/> sections into system prompts. Used by both
/// the standard path (<see cref="AgentPromptComposer"/>) and the streaming path
/// (<c>StreamingAgentExecutionService</c>) so guardrails render byte-identically on either:
/// agent-level section first, then the task's own, tool-specific rules gated by the executing
/// agent's actual tools.
/// </summary>
public static class GuardrailsPromptRenderer
{
    /// <summary>
    /// Appends the agent's guardrails section followed by the task's own. Each section is a
    /// no-op when its config is null or empty.
    /// </summary>
    public static void AppendAgentAndTaskGuardrails(StringBuilder prompt, DomainAgent agent, CrewTask task)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(task);

        var agentToolNames = agent.Tools.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        AppendSection(prompt, agent.Guardrails, agentToolNames);
        AppendSection(prompt, task.Guardrails, agentToolNames);
    }

    /// <summary>
    /// Appends a single guardrails section: header, numbered global rules, then tool-specific
    /// rules (numbering continues) filtered to <paramref name="agentToolNames"/>.
    /// </summary>
    public static void AppendSection(
        StringBuilder prompt,
        GuardrailsConfig? guardrails,
        IReadOnlySet<string> agentToolNames)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(agentToolNames);

        if (guardrails == null || guardrails.IsEmpty)
            return;

        prompt.AppendLine();

        var header = guardrails.Header ?? GuardrailDefaults.DefaultHeader;
        prompt.AppendLine(header);

        var ruleIndex = 1;
        foreach (var rule in guardrails.Rules)
        {
            prompt.AppendLine(FormattableString.Invariant($"{ruleIndex}. {rule}"));
            ruleIndex++;
        }

        foreach (var kvp in guardrails.ToolRules)
        {
            if (!agentToolNames.Contains(kvp.Key))
                continue;

            foreach (var rule in kvp.Value)
            {
                prompt.AppendLine(FormattableString.Invariant($"{ruleIndex}. [{kvp.Key}] {rule}"));
                ruleIndex++;
            }
        }
    }
}
