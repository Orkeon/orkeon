using System.Text;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Constants.Agent;
using Orkeon.Domain.Task;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// Rendering of <see cref="GuardrailsConfig"/> sections into system prompts, for
/// <see cref="AgentPromptComposer"/> — the one composer since a streamed run goes through the same
/// agent loops (GAP-32): agent-level section first, then the task's own, tool-specific rules gated
/// by the tools the agent holds for this task (<see cref="TaskToolbelt"/>).
/// </summary>
public static class GuardrailsPromptRenderer
{
    /// <summary>
    /// Appends the agent's guardrails section followed by the task's own. Each section is a
    /// no-op when its config is null or empty.
    /// </summary>
    /// <param name="prompt">The prompt being built.</param>
    /// <param name="agent">The executing agent.</param>
    /// <param name="task">The task being run.</param>
    /// <param name="toolbelt">The task's toolbelt, from <see cref="TaskToolbelt.Compose"/>: tool rules apply to these tools.</param>
    public static void AppendAgentAndTaskGuardrails(
        StringBuilder prompt, DomainAgent agent, CrewTask task, IReadOnlyList<Domain.Tools.IBaseTool> toolbelt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(toolbelt);

        var agentToolNames = toolbelt.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
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
