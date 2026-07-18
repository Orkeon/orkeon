using DomainAgent = Orkeon.Domain.Agent.Agent;
using System.Text;
using Orkeon.Application.Context;
using Orkeon.Application.Constants.Orchestration;
using Orkeon.Domain.Constants.Agent;
using Orkeon.Domain.Task;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// Stateless system/user prompt composition for agent task execution.
/// Extracted verbatim from <see cref="ExecutionOrchestrator"/> (R4.1): role section,
/// tools section (with text tool-call instructions for non-native providers),
/// guardrails, response template, task interpolation, context variables and
/// previous-output sections.
/// </summary>
internal static class AgentPromptComposer
{
    /// <summary>
    /// Maximum number of characters from previous task outputs to include in prompt context.
    /// Prevents excessive prompt sizes when previous tasks produce very large outputs.
    /// </summary>
    private const int MaxPreviousOutputContextChars = 8000;

    internal static string BuildSystemPrompt(DomainAgent agent, CrewTask task, bool supportsNativeToolCalling)
    {
        var prompt = new StringBuilder();

        AppendRoleSection(prompt, agent);
        AppendToolsSection(prompt, agent, supportsNativeToolCalling);
        // Agent-level guardrails first, then the task's own — both apply, agent rules before task rules.
        AppendGuardrails(prompt, agent);
        AppendTaskGuardrails(prompt, task, agent);
        AppendResponseTemplate(prompt, agent);

        return prompt.ToString();
    }

    private static void AppendRoleSection(StringBuilder prompt, DomainAgent agent)
    {
        if (!string.IsNullOrEmpty(agent.SystemTemplate))
        {
            prompt.AppendLine(agent.SystemTemplate);
            return;
        }

        prompt.AppendLine(FormattableString.Invariant($"You are {agent.Role}."));
        prompt.AppendLine(FormattableString.Invariant($"Your goal is: {agent.Goal}"));
        if (agent.Backstory is not null)
            prompt.AppendLine(FormattableString.Invariant($"Background: {agent.Backstory}"));
    }

    internal static void AppendToolsSection(StringBuilder prompt, DomainAgent agent, bool supportsNativeToolCalling)
    {
        if (agent.Tools.Count == 0)
            return;

        prompt.AppendLine();
        prompt.AppendLine("## Available Tools");
        prompt.AppendLine();
        foreach (var tool in agent.Tools)
        {
            prompt.AppendLine(FormattableString.Invariant($"- **{tool.Name}**: {tool.Description}"));
            AppendToolParameters(prompt, tool);
        }

        if (!supportsNativeToolCalling)
            AppendTextToolCallInstructions(prompt);

        prompt.AppendLine();
    }

    /// <summary>
    /// Appends the required/optional parameter lines for a single tool entry.
    /// No-op when the tool has no schema parameters.
    /// </summary>
    private static void AppendToolParameters(StringBuilder prompt, Domain.Tools.IBaseTool tool)
    {
        if (tool.Schema?.Parameters is not { Count: > 0 } parameters)
            return;

        var required = parameters.Where(p => p.Value.Required).Select(p => p.Key).ToList();
        var optional = parameters.Where(p => !p.Value.Required).ToList();

        if (required.Count > 0)
            prompt.AppendLine(FormattableString.Invariant($"  Required: {string.Join(", ", required)}"));

        if (optional.Count > 0)
        {
            var optParts = optional.Select(p =>
                p.Value.Default != null ? $"{p.Key} (default: {p.Value.Default})" : p.Key);
            prompt.AppendLine(FormattableString.Invariant($"  Optional: {string.Join(", ", optParts)}"));
        }
    }

    /// <summary>
    /// Appends the "How to Call Tools" section for providers that do not support native function calling.
    /// </summary>
    private static void AppendTextToolCallInstructions(StringBuilder prompt)
    {
        prompt.AppendLine();
        prompt.AppendLine("## How to Call Tools");
        prompt.AppendLine();
        prompt.AppendLine("When you need to use a tool, you MUST emit a tool call block using this exact format:");
        prompt.AppendLine();
        prompt.AppendLine("[TOOL_CALL]{tool => \"tool_name\", args => {--param1 \"value1\" --param2 \"value2\"}}[/TOOL_CALL]");
        prompt.AppendLine();
        prompt.AppendLine("Rules:");
        prompt.AppendLine("- Always use the exact tool name from the list above.");
        prompt.AppendLine("- Each parameter is prefixed with -- followed by a space and the value in double quotes.");
        prompt.AppendLine("- You can call ONE tool per [TOOL_CALL] block. To call multiple tools, emit multiple blocks.");
        prompt.AppendLine("- After emitting a [TOOL_CALL] block, STOP and wait for the tool result before continuing.");
        prompt.AppendLine("- Do NOT describe what you would do — actually call the tool.");
        prompt.AppendLine();
        prompt.AppendLine("Example:");
        prompt.AppendLine("[TOOL_CALL]{tool => \"directory_read\", args => {--path \"/src\"}}[/TOOL_CALL]");
    }

    /// <summary>
    /// Appends configurable guardrails to the system prompt based on the agent's <see cref="Domain.Agent.GuardrailsConfig"/>.
    /// If the agent has no guardrails configured, this method is a no-op.
    /// </summary>
    private static void AppendGuardrails(StringBuilder prompt, DomainAgent agent)
    {
        var agentToolNames = agent.Tools.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        AppendGuardrailsSection(prompt, agent.Guardrails, agentToolNames);
    }

    /// <summary>
    /// Appends the task's own guardrails (P2-O-04) as a separate section after the agent's, gating
    /// tool-specific rules by the executing agent's actual tools (mirrors the agent-level rules).
    /// No-op when the task has none.
    /// </summary>
    private static void AppendTaskGuardrails(StringBuilder prompt, CrewTask task, DomainAgent agent)
    {
        var agentToolNames = agent.Tools.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        AppendGuardrailsSection(prompt, task.Guardrails, agentToolNames);
    }

    private static void AppendGuardrailsSection(
        StringBuilder prompt,
        Domain.Agent.GuardrailsConfig? guardrails,
        HashSet<string> agentToolNames)
    {
        if (guardrails == null || guardrails.IsEmpty)
            return;

        prompt.AppendLine();

        // Header
        var header = guardrails.Header ?? GuardrailDefaults.DefaultHeader;
        prompt.AppendLine(header);

        // Global rules (numbered)
        var ruleIndex = 1;
        foreach (var rule in guardrails.Rules)
        {
            prompt.AppendLine(FormattableString.Invariant($"{ruleIndex}. {rule}"));
            ruleIndex++;
        }

        // Tool-specific rules: only rendered when the agent actually has the tool
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

    private static void AppendResponseTemplate(StringBuilder prompt, DomainAgent agent)
    {
        if (string.IsNullOrEmpty(agent.ResponseTemplate))
            return;

        prompt.AppendLine();
        prompt.AppendLine(agent.ResponseTemplate);
    }

    internal static string BuildUserPrompt(CrewTask task, SimpleExecutionContext context)
    {
        var (description, expectedOutput) = InterpolateTaskFields(task, context);

        var prompt = new StringBuilder();
        prompt.AppendLine(PromptDefaults.TaskSectionHeader);
        prompt.AppendLine(description);
        prompt.AppendLine();
        prompt.AppendLine(FormattableString.Invariant($"{PromptDefaults.ExpectedOutputPrefix}{expectedOutput}"));

        AppendContextVariables(prompt, context);
        AppendPreviousOutputs(prompt, context);

        return prompt.ToString();
    }

    /// <summary>
    /// Applies <see cref="InterpolateVariables(string, Dictionary{string, string})"/> to the task description
    /// and expected output using the supplied execution context variables.
    /// </summary>
    private static (string Description, string ExpectedOutput) InterpolateTaskFields(
        CrewTask task, SimpleExecutionContext? context)
    {
        var description = task.Description.ToString();
        var expectedOutput = task.ExpectedOutput.ToString();

        if (context?.Variables?.Count > 0)
        {
            description = InterpolateVariables(description, context.Variables);
            expectedOutput = InterpolateVariables(expectedOutput, context.Variables);
        }

        return (description, expectedOutput);
    }

    /// <summary>
    /// Appends the <c>Context Variables</c> section (when any variables are set) to the user prompt.
    /// </summary>
    private static void AppendContextVariables(StringBuilder prompt, SimpleExecutionContext? context)
    {
        if (!(context?.Variables?.Count > 0))
            return;

        prompt.AppendLine();
        prompt.AppendLine(PromptDefaults.ContextVariablesHeader);
        foreach (var kvp in context.Variables)
        {
            prompt.AppendLine(FormattableString.Invariant($"- {kvp.Key}: {kvp.Value}"));
        }
    }

    /// <summary>
    /// Appends the <c>Previous Outputs</c> section — skipping empty entries and truncating once
    /// <see cref="MaxPreviousOutputContextChars"/> is exceeded so downstream prompts stay bounded.
    /// </summary>
    private static void AppendPreviousOutputs(StringBuilder prompt, SimpleExecutionContext? context)
    {
        if (!(context?.PreviousOutputs?.Count > 0))
            return;

        prompt.AppendLine();
        prompt.AppendLine(PromptDefaults.PreviousOutputsHeader);

        var totalChars = 0;
        foreach (var prevOutput in context.PreviousOutputs)
        {
            if (string.IsNullOrWhiteSpace(prevOutput.Content))
                continue;

            var remaining = MaxPreviousOutputContextChars - totalChars;
            if (remaining <= 0)
            {
                prompt.AppendLine(PromptDefaults.TruncationMessage);
                break;
            }

            var content = prevOutput.Content;
            if (content.Length > remaining)
                content = content[..remaining] + "... [truncated]";

            prompt.AppendLine(FormattableString.Invariant($"--- Task {prevOutput.TaskId} (success={prevOutput.Success}) ---"));
            prompt.AppendLine(content);
            totalChars += content.Length;
        }
    }

    /// <summary>
    /// Replaces <c>{key}</c> placeholders in the template with corresponding variable values.
    /// Unmatched placeholders are left as-is.
    /// </summary>
    private static string InterpolateVariables(string template, Dictionary<string, string> variables)
    {
        if (string.IsNullOrEmpty(template) || variables.Count == 0)
            return template;

        var result = template;
        foreach (var kvp in variables)
        {
            result = result.Replace($"{{{kvp.Key}}}", kvp.Value, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }
}
