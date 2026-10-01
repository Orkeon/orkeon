using DomainAgent = Orkeon.Domain.Agent.Agent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew.Grammar;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// Builds the per-call <see cref="ChatOptions"/> for the IChatClient path: takes the task's
/// toolbelt from <see cref="TaskToolbelt"/> (agent + task tools + auto-injected <c>human_input</c>), wires
/// native function calling, attaches the structured-output grammar and JSON schema, and applies the
/// agent-level then task-level LLM overrides. Extracted verbatim from
/// <see cref="ExecutionOrchestrator"/> (R4.1).
/// </summary>
internal sealed class ChatOptionsComposer
{
    /// <summary>The <c>json_schema.name</c> a deliverable schema travels under.</summary>
    private const string StructuredOutputSchemaName = "structured_output";

    private readonly ILogger _logger;
    private readonly IEnumerable<Domain.Tools.IBaseTool>? _registeredTools;
    private readonly Domain.FileSystem.IFileSystemService _fileSystem;
    private readonly IToolInvocationPipeline _toolInvocation;

    internal ChatOptionsComposer(
        ILogger logger,
        IEnumerable<Domain.Tools.IBaseTool>? registeredTools,
        Domain.FileSystem.IFileSystemService fileSystem,
        IToolInvocationPipeline toolInvocation)
    {
        _logger = logger;
        _registeredTools = registeredTools;
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
        _toolInvocation = toolInvocation;
    }

    /// <summary>
    /// Builds ChatOptions with native function calling tools for the given agent.
    /// Returns the options and the list of resolved tools.
    /// </summary>
    internal async System.Threading.Tasks.Task<(ChatOptions options, List<Domain.Tools.IBaseTool> availableTools)> BuildChatOptionsAsync(
        DomainAgent agent, CrewTask task, CancellationToken cancellationToken)
    {
        var options = new ChatOptions();
        // Agent tools, then the task's own, then human_input when the task asks for it and the
        // host registered the tool — one composition shared with every loop (GAP-07).
        var availableTools = TaskToolbelt.Compose(agent, task, _registeredTools).ToList();

        if (availableTools.Count > 0)
        {
            options.Tools = availableTools
                .Select(t => (AITool)AIFunctionFactory.Create(
                    // Only invoked when the host wraps the chat client with automatic function
                    // invocation; the agent loop itself dispatches through ChatToolDispatcher.
                    // Either way the call goes through the invocation pipeline (GAP-09).
                    method: async (AIFunctionArguments args, CancellationToken ct) =>
                    {
                        var parameters = args
                            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                        var outcome = await _toolInvocation.InvokeAsync(
                            new ToolInvocation(t, parameters, ChatToolDispatcher.CallerOf(agent, task)), ct).ConfigureAwait(false);
                        return outcome.ConversationText;
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
        // A pinned cap is forwarded whatever its value; an unpinned one is null and stays
        // with the provider, which resolves the model's documented maximum (LLM-10). The
        // first version compared against the old 4096 default to guess which was which.
        if (llm.MaxTokens is > 0)
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
    /// JSON schema, stash two forms of it on <see cref="ChatOptions.AdditionalProperties"/>: a GBNF
    /// grammar under <see cref="Common.DTOs.LlmChatOptionsKeys.GrammarGbnf"/>, for an endpoint
    /// configured to take one (<c>Llm:Grammar</c>), and a <c>json_schema</c> response format under
    /// <see cref="Common.DTOs.LlmChatOptionsKeys.StructuredOutput"/>, for a provider that declares
    /// JSON Schema support. The adapter (Infrastructure) picks the one the provider honours.
    /// Failures are logged and swallowed — the LLM call still proceeds, and the resolver's JSON
    /// safety check catches any drift.
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
            options.AdditionalProperties[Common.DTOs.LlmChatOptionsKeys.GrammarGbnf] = grammar;
            // Not strict: OpenAI's strict mode refuses a schema that leaves an object open or a
            // property optional, and a deliverable schema is the author's, not shaped for it.
            options.AdditionalProperties[Common.DTOs.LlmChatOptionsKeys.StructuredOutput] =
                Domain.SharedKernel.ValueObjects.LlmResponseFormat.JsonSchema(
                    StructuredOutputSchemaName, schemaText!, strict: false);
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
