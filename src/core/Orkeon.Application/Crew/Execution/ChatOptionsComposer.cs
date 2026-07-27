using DomainAgent = Orkeon.Domain.Agent.Agent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew.Grammar;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// Builds the per-call <see cref="ChatOptions"/> for the IChatClient path: resolves the
/// agent's toolbelt (registry + agent-owned + auto-injected <c>human_input</c>), wires
/// native function calling, attaches the structured-output GBNF grammar, and applies the
/// agent-level then task-level LLM overrides. Extracted verbatim from
/// <see cref="ExecutionOrchestrator"/> (R4.1).
/// </summary>
internal sealed class ChatOptionsComposer
{
    private readonly ILogger _logger;
    private readonly IEnumerable<Domain.Tools.IBaseTool>? _registeredTools;
    private readonly Domain.FileSystem.IFileSystemService _fileSystem;

    internal ChatOptionsComposer(
        ILogger logger,
        IEnumerable<Domain.Tools.IBaseTool>? registeredTools,
        Domain.FileSystem.IFileSystemService fileSystem)
    {
        _logger = logger;
        _registeredTools = registeredTools;
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Builds ChatOptions with native function calling tools for the given agent.
    /// Returns the options and the list of resolved tools.
    /// </summary>
    internal async System.Threading.Tasks.Task<(ChatOptions options, List<Domain.Tools.IBaseTool> availableTools)> BuildChatOptionsAsync(
        DomainAgent agent, CrewTask task, CancellationToken cancellationToken)
    {
        var options = new ChatOptions();
        var agentToolNames = agent.Tools.Select(t => t.Name).ToHashSet();

        // Start with tools from the global DI registry that match the agent's tool list
        var registryTools = _registeredTools?
            .Where(t => agentToolNames.Contains(t.Name))
            .ToList() ?? [];

        // Include agent-owned tools not found in the registry (e.g. delegation tools
        // added dynamically by AgentDelegationToolsProvider at runtime).
        var registryToolNames = registryTools.Select(t => t.Name).ToHashSet();
        var agentOnlyTools = agent.Tools
            .Where(t => !registryToolNames.Contains(t.Name))
            .ToList();

        var availableTools = registryTools.Concat(agentOnlyTools).ToList();

        // When the task declares humanInput=true, auto-inject the registered
        // human_input tool into the toolbelt so the agent can solicit user
        // input without having to declare the tool on the agent. If no
        // human_input tool is registered (no IHumanInputProvider wired), the
        // task runs as-is — the YAML flag becomes a no-op for that run.
        if (task.HumanInput)
        {
            var humanInputTool = _registeredTools?
                .FirstOrDefault(t => string.Equals(t.Name, "human_input", StringComparison.OrdinalIgnoreCase));
            if (humanInputTool is not null && !availableTools.Any(t => string.Equals(t.Name, "human_input", StringComparison.OrdinalIgnoreCase)))
            {
                availableTools.Add(humanInputTool);
            }
        }

        if (availableTools.Count > 0)
        {
            options.Tools = availableTools
                .Select(t => (AITool)AIFunctionFactory.Create(
                    method: async (AIFunctionArguments args, CancellationToken ct) =>
                    {
                        var parameters = args
                            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                        var request = new Domain.Tools.Protocol.ToolCallRequest(t.Name, parameters);
                        var response = await t.CallAsync(request, ct).ConfigureAwait(false);
                        return response.Success ? response.Result?.ToString() ?? string.Empty : $"Error: {response.Error}";
                    },
                    name: t.Name,
                    description: t.Schema.Description))
                .ToList();
            options.ToolMode = ChatToolMode.Auto;

            // Propagate ToolSchema list for the adapter path (ILlmProvider → IChatClient bridge).
            // The adapter reads these from AdditionalProperties to populate LlmConfig.Tools.
            options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
            options.AdditionalProperties["orkeon:tool_schemas"] =
                (IReadOnlyList<Domain.Tools.Protocol.ToolSchema>)availableTools
                    .Select(t => t.Schema).ToList().AsReadOnly();
            options.AdditionalProperties["orkeon:tool_mode"] = Domain.Tools.Protocol.ToolCallMode.Auto;
        }

        await TryAttachStructuredOutputGrammarAsync(options, task, cancellationToken).ConfigureAwait(false);

        ApplyAgentLlmOverrides(options, agent);
        ApplyTaskLlmOverrides(options, task);

        return (options, availableTools);
    }

    /// <summary>
    /// When the agent carries a per-agent <see cref="Domain.SharedKernel.ValueObjects.LlmConfig"/>,
    /// apply its fields (Model, Temperature, MaxTokens, TopP, Thinking) onto the chat options
    /// so the adapter forwards them to the LLM provider on this call only. Experiment 07
    /// friction #7: prior behavior parsed the YAML but never propagated the override, so a
    /// supervisor / planner / classifier mix on the same crew all ran on the same crew-default
    /// model.
    /// </summary>
    private static void ApplyAgentLlmOverrides(ChatOptions options, DomainAgent agent)
    {
        var llm = agent.LlmConfig;
        if (llm is null) return;

        if (!string.IsNullOrWhiteSpace(llm.Model))
            options.ModelId = llm.Model;

        // The orchestrator only forwards explicit overrides; leave nullable adapter-side
        // fields untouched when the agent's config matches the LlmConfig default, so the
        // chat client's own defaults still apply when the YAML omits a field.
        if (llm.Temperature != Domain.Constants.Llm.LlmDefaults.DefaultTemperature)
            options.Temperature = (float)llm.Temperature;
        if (llm.MaxTokens > 0 && llm.MaxTokens != Domain.Constants.Llm.LlmDefaults.DefaultContextWindowTokens)
            options.MaxOutputTokens = llm.MaxTokens;
        if (llm.TopP != 1.0)
            options.TopP = (float)llm.TopP;

        if (llm.Thinking is not null)
        {
            options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
            options.AdditionalProperties[Application.Common.DTOs.LlmChatOptionsKeys.Thinking] = llm.Thinking;
        }

        if (llm.ResponseFormat is not null)
        {
            options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
            options.AdditionalProperties[Application.Common.DTOs.LlmChatOptionsKeys.ResponseFormat] = llm.ResponseFormat;
        }

        if (llm.Cache is not null)
        {
            options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
            options.AdditionalProperties[Application.Common.DTOs.LlmChatOptionsKeys.Cache] = llm.Cache;
        }
    }

    /// <summary>
    /// Applies the per-task <see cref="Domain.SharedKernel.ValueObjects.LlmConfigOverride"/> onto the
    /// chat options so the adapter forwards it to the provider. Task-level wins over agent-level
    /// (latest writer to <see cref="ChatOptions"/> takes effect). Without this, the YAML
    /// <c>llm_override:</c> block is parsed and stored on <c>CrewTask.LlmOverride</c> but
    /// never reaches the wire — <c>response_format: json_object</c> silently drops.
    /// </summary>
    private static void ApplyTaskLlmOverrides(ChatOptions options, CrewTask task)
    {
        var ov = task.LlmOverride;
        if (ov is null) return;

        if (ov.Temperature.HasValue)
            options.Temperature = (float)ov.Temperature.Value;
        if (ov.MaxTokens.HasValue && ov.MaxTokens.Value > 0)
            options.MaxOutputTokens = ov.MaxTokens.Value;
        if (ov.TopP.HasValue)
            options.TopP = (float)ov.TopP.Value;

        if (ov.Thinking is not null)
        {
            options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
            options.AdditionalProperties[Application.Common.DTOs.LlmChatOptionsKeys.Thinking] = ov.Thinking;
        }

        if (ov.ResponseFormat is not null)
        {
            options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
            options.AdditionalProperties[Application.Common.DTOs.LlmChatOptionsKeys.ResponseFormat] = ov.ResponseFormat;
        }

        if (ov.Cache is not null)
        {
            options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
            options.AdditionalProperties[Application.Common.DTOs.LlmChatOptionsKeys.Cache] = ov.Cache;
        }
    }

    /// <summary>
    /// When the task declares a <c>structured_output</c> deliverable with an inline or file-based
    /// JSON schema, convert the schema to a GBNF grammar and stash it on
    /// <see cref="ChatOptions.AdditionalProperties"/> under <c>orkeon:grammar_gbnf</c> so that the
    /// adapter (Infrastructure) can forward it to the llama.cpp backend. Failures are logged and
    /// swallowed — the LLM call still proceeds, and the resolver's JSON safety check catches any
    /// drift.
    /// </summary>
    private async System.Threading.Tasks.Task TryAttachStructuredOutputGrammarAsync(
        ChatOptions options, CrewTask task, CancellationToken cancellationToken)
    {
        var deliv = task.Deliverable;
        if (deliv is null || deliv.Source != DeliverableSource.StructuredOutput) return;

        var schemaText = await LoadSchemaTextAsync(deliv, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(schemaText)) return;

        try
        {
            var grammar = JsonSchemaToGbnfConverter.Convert(schemaText!);
            options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
            options.AdditionalProperties["orkeon:grammar_gbnf"] = grammar;
        }
        catch (Exception ex) when (ex is ArgumentException or System.Text.Json.JsonException or InvalidOperationException)
        {
            ExecutionLog.LogGrammarConversionFailed(_logger, task.Id.ToString(), deliv.Path, ex.Message);
        }
    }

    private async System.Threading.Tasks.Task<string?> LoadSchemaTextAsync(
        TaskDeliverable deliv, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(deliv.SchemaInline)) return deliv.SchemaInline;
        if (string.IsNullOrWhiteSpace(deliv.SchemaPath)) return null;

        try
        {
            return await _fileSystem.TryReadAllTextAsync(deliv.SchemaPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is Domain.FileSystem.FileAccessDeniedException or IOException)
        {
            ExecutionLog.LogSchemaPathReadFailed(_logger, deliv.SchemaPath, ex.Message);
            return null;
        }
    }
}
