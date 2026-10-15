using DomainAgent = Orkeon.Domain.Agent.Agent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew.Grammar;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// Builds the per-call <see cref="ChatOptions"/> for the IChatClient path: takes the task's
/// toolbelt from <see cref="TaskToolbelt"/> (agent + task tools + auto-injected <c>human_input</c>), wires
/// native function calling, attaches the structured-output grammar and JSON schema, and translates onto
/// the options the LLM configuration <see cref="LlmConfigResolver"/> merges once (agent, then the
/// task's override). Extracted verbatim from
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

        // The task's llm_override is merged once, by the resolver, over the agent's configuration.
        ApplyLlmConfig(options, LlmConfigResolver.Resolve(
            agent.LlmConfig ?? LlmConfig.OnProfile(), task.LlmOverride));

        return (options, availableTools);
    }

    /// <summary>
    /// Translates the merged <see cref="LlmConfig"/> (Model, Temperature, MaxTokens, TopP, the
    /// penalties, Seed, StopSequences, Thinking…) onto the chat options so the adapter forwards
    /// each field to the LLM provider as written, on this call only.
    /// </summary>
    private static void ApplyLlmConfig(ChatOptions options, LlmConfig llm)
    {
        if (!string.IsNullOrWhiteSpace(llm.Model))
            options.ModelId = llm.Model;

        // What the agent's config sets is forwarded whatever its value; what it leaves unset
        // (null) stays with the provider — its profile's value, else the model's own. The
        // temperature and top_p used to be compared with 0.7 and 1.0, the engine's defaults, so
        // an agent asking for exactly those ran on its profile's (GAP-36), as an output cap
        // compared with the old 4096 once did (LLM-10).
        if (llm.Temperature is { } temperature)
            options.Temperature = (float)temperature;
        if (llm.MaxTokens is > 0)
            options.MaxOutputTokens = llm.MaxTokens;
        if (llm.TopP is { } topP)
            options.TopP = (float)topP;
        // The penalties have no unset value: 0 is every vendor's default, so a penalty is set
        // when it is not 0. A provider whose dialect cannot send one says so (GAP-36, decision 8).
        if (llm.FrequencyPenalty != 0.0)
            options.FrequencyPenalty = (float)llm.FrequencyPenalty;
        if (llm.PresencePenalty != 0.0)
            options.PresencePenalty = (float)llm.PresencePenalty;
        if (llm.Seed is { } seed)
            options.Seed = seed;
        if (llm.StopSequences is { Count: > 0 } stopSequences)
            options.StopSequences = [.. stopSequences];

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
