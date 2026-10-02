using DomainAgent = Orkeon.Domain.Agent.Agent;
using System.Globalization;
using System.Text;
using Orkeon.Application.Context;
using Orkeon.Application.Constants.Orchestration;
using Orkeon.Application.Memory;
using Orkeon.Domain.Constants.Crew;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// Stateless system/user prompt composition for agent task execution.
/// Extracted verbatim from <see cref="ExecutionOrchestrator"/> (R4.1): role section,
/// tools section (with text tool-call instructions for non-native providers),
/// guardrails, response template, task interpolation, the task's plan (GAP-31), context
/// variables, previous-output and recalled-memory (GAP-30) sections.
/// </summary>
internal static class AgentPromptComposer
{
    /// <summary>
    /// Maximum number of characters from previous task outputs to include in prompt context.
    /// Prevents excessive prompt sizes when previous tasks produce very large outputs.
    /// </summary>
    private const int MaxPreviousOutputContextChars = 8000;

    /// <summary>How much of a recalled memory's task description its heading line shows.</summary>
    private const int MaxMemoryTaskDescriptionChars = 80;

    /// <param name="agent">The executing agent.</param>
    /// <param name="task">The task being run.</param>
    /// <param name="toolbelt">The task's toolbelt, from <see cref="TaskToolbelt.Compose"/>.</param>
    /// <param name="supportsNativeToolCalling">Whether the provider calls tools natively.</param>
    internal static string BuildSystemPrompt(
        DomainAgent agent, CrewTask task, IReadOnlyList<Domain.Tools.IBaseTool> toolbelt, bool supportsNativeToolCalling)
    {
        var prompt = new StringBuilder();

        AppendRoleSection(prompt, agent);
        AppendToolsSection(prompt, toolbelt, supportsNativeToolCalling);
        // Agent-level guardrails first, then the task's own — both apply, agent rules before task
        // rules. Shared with the streaming path so both render identically.
        GuardrailsPromptRenderer.AppendAgentAndTaskGuardrails(prompt, agent, task, toolbelt);
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

    internal static void AppendToolsSection(
        StringBuilder prompt, IReadOnlyList<Domain.Tools.IBaseTool> toolbelt, bool supportsNativeToolCalling)
    {
        if (toolbelt.Count == 0)
            return;

        prompt.AppendLine();
        prompt.AppendLine("## Available Tools");
        prompt.AppendLine();
        foreach (var tool in toolbelt)
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

    private static void AppendResponseTemplate(StringBuilder prompt, DomainAgent agent)
    {
        if (string.IsNullOrEmpty(agent.ResponseTemplate))
            return;

        prompt.AppendLine();
        prompt.AppendLine(agent.ResponseTemplate);
    }

    /// <param name="task">The task being run.</param>
    /// <param name="context">The execution context: variables, previous outputs, recalled memories.</param>
    /// <param name="knowledgeContext">The retrieved knowledge block, when the agent has knowledge attached.</param>
    /// <param name="plan">
    /// The task's plan, when the crew plans (GAP-31): what <see cref="CrewPlanScope"/> holds for the task.
    /// Null or blank leaves the prompt byte-identical to the prompt of a crew that does not plan.
    /// </param>
    internal static string BuildUserPrompt(
        CrewTask task, SimpleExecutionContext context, string? knowledgeContext = null, string? plan = null)
    {
        var (description, expectedOutput) = InterpolateTaskFields(task, context);

        var prompt = new StringBuilder();
        prompt.AppendLine(PromptDefaults.TaskSectionHeader);
        prompt.AppendLine(description);
        prompt.AppendLine();
        prompt.AppendLine(FormattableString.Invariant($"{PromptDefaults.ExpectedOutputPrefix}{expectedOutput}"));
        AppendDeliverableInstruction(prompt, task);

        AppendPlan(prompt, plan);
        AppendContextVariables(prompt, context);
        AppendPreviousOutputs(prompt, context);
        AppendRecalledMemories(prompt, context);
        AppendKnowledgeContext(prompt, knowledgeContext);

        return prompt.ToString();
    }

    /// <summary>
    /// Appends the task's plan (GAP-31) right after the task, before the context variables:
    /// <see cref="PromptDefaults.PlanSectionHeader"/>, then the plan, cut at
    /// <see cref="PlanningDefaults.MaxPlanChars"/> — the plan travels with every turn of the task's
    /// loop. No-op without a plan: the prompt is then byte-identical to the prompt of a crew that
    /// does not plan.
    /// </summary>
    private static void AppendPlan(StringBuilder prompt, string? plan)
    {
        if (string.IsNullOrWhiteSpace(plan))
            return;

        var text = plan.Trim();
        if (text.Length > PlanningDefaults.MaxPlanChars)
            text = string.Concat(text.AsSpan(0, PlanningDefaults.MaxPlanChars), PlanningDefaults.TruncationMarker);

        prompt.AppendLine();
        prompt.AppendLine(PromptDefaults.PlanSectionHeader);
        prompt.AppendLine(text);
    }

    /// <summary>
    /// Appends the crew's recalled memories (GAP-30), after the previous outputs and before the
    /// retrieved knowledge: <see cref="PromptDefaults.MemoriesHeader"/>, then for each memory a
    /// <c>--- date · role · task ---</c> line followed by its content, already cut to the recall's
    /// budget. No-op when the context carries none — the prompt is then byte-identical to the
    /// prompt of a crew without memory.
    /// </summary>
    private static void AppendRecalledMemories(StringBuilder prompt, SimpleExecutionContext context)
    {
        if (context.RecalledMemories is not { Count: > 0 } memories)
            return;

        prompt.AppendLine();
        prompt.AppendLine(PromptDefaults.MemoriesHeader);
        foreach (var memory in memories)
        {
            prompt.AppendLine(MemoryHeading(memory));
            prompt.AppendLine(memory.Content);
        }
    }

    /// <summary>
    /// <c>--- 2026-09-30 · Analyst · Summarize the weekly news ---</c>: when the memory was stored,
    /// the role of the agent that wrote it and the task it answered, on one line and cut to
    /// <see cref="MaxMemoryTaskDescriptionChars"/> characters; what is unknown is left out.
    /// </summary>
    private static string MemoryHeading(RecalledMemory memory)
    {
        var parts = new List<string> { memory.StoredAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        if (!string.IsNullOrWhiteSpace(memory.AgentRole))
            parts.Add(OneLine(memory.AgentRole));
        if (!string.IsNullOrWhiteSpace(memory.TaskDescription))
        {
            var description = OneLine(memory.TaskDescription);
            parts.Add(description.Length > MaxMemoryTaskDescriptionChars
                ? string.Concat(description.AsSpan(0, MaxMemoryTaskDescriptionChars - 1), "…")
                : description);
        }

        return $"--- {string.Join(" · ", parts)} ---";
    }

    /// <summary>The text on one line: every run of whitespace, line breaks included, as one space.</summary>
    private static string OneLine(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Names the deliverable path when the task's contract says the agent writes it
    /// itself (<see cref="DeliverableSource.ToolCall"/>). Without this line the agent
    /// only sees the prose description and guesses paths — the owner's forge trial
    /// showed a writer burning three denied file_write calls before finding /output.
    /// The other sources are persisted by the framework, so naming a path there would
    /// invite a redundant (and possibly conflicting) manual write.
    /// </summary>
    private static void AppendDeliverableInstruction(StringBuilder prompt, CrewTask task)
    {
        if (task.Deliverable is not { Source: DeliverableSource.ToolCall, Path.Length: > 0 } deliverable)
            return;

        prompt.AppendLine(FormattableString.Invariant(
            $"Deliverable: write the final result to '{deliverable.Path}' using the file_write tool."));
    }

    /// <summary>
    /// Appends the retrieved-knowledge context block (RAG-03/C4) after the task
    /// context sections. No-op when <paramref name="knowledgeContext"/> is null or
    /// blank — the prompt is then byte-identical to the pre-RAG output.
    /// </summary>
    private static void AppendKnowledgeContext(StringBuilder prompt, string? knowledgeContext)
    {
        if (string.IsNullOrWhiteSpace(knowledgeContext))
            return;

        prompt.AppendLine();
        prompt.AppendLine(knowledgeContext.TrimEnd());
    }

    /// <summary>
    /// Builds the retrieval query text for knowledge augmentation (RAG-03/C4):
    /// the interpolated task description and expected output, plus the context
    /// variables. Previous task outputs are deliberately excluded — they can be
    /// arbitrarily large and would drown the embedding signal.
    /// </summary>
    internal static string BuildKnowledgeQueryText(CrewTask task, SimpleExecutionContext? context)
    {
        var (description, expectedOutput) = InterpolateTaskFields(task, context);

        var query = new StringBuilder();
        query.AppendLine(description);
        query.Append(PromptDefaults.ExpectedOutputPrefix).Append(expectedOutput);

        if (context?.Variables?.Count > 0)
        {
            foreach (var kvp in context.Variables)
            {
                query.AppendLine();
                query.Append(FormattableString.Invariant($"- {kvp.Key}: {kvp.Value}"));
            }
        }

        return query.ToString();
    }

    /// <summary>
    /// The task's description with the run's variables in it — what a memory of the task records
    /// (GAP-30).
    /// </summary>
    internal static string InterpolateDescription(CrewTask task, SimpleExecutionContext? context) =>
        InterpolateTaskFields(task, context).Description;

    /// <summary>
    /// The task description and expected output with the execution context's variables in them
    /// (<see cref="TaskTextInterpolation.Interpolate"/>, which the crew's planner reads the tasks with too).
    /// </summary>
    private static (string Description, string ExpectedOutput) InterpolateTaskFields(
        CrewTask task, SimpleExecutionContext? context) =>
        (TaskTextInterpolation.Interpolate(task.Description.ToString(), context?.Variables),
         TaskTextInterpolation.Interpolate(task.ExpectedOutput.ToString(), context?.Variables));

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
}
