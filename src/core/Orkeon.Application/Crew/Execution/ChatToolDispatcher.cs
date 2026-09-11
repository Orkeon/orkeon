using DomainAgent = Orkeon.Domain.Agent.Agent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Constants.Orchestration;
using Orkeon.Constants.Llm;
using Orkeon.Domain.Constants.Agent;
using Orkeon.Domain.Task;
using Orkeon.Domain.Tools;
using System.Text;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// Executes tool calls on the IChatClient path — native <see cref="FunctionCallContent"/>
/// dispatch and text-fallback <c>[TOOL_CALL]</c> dispatch — recording
/// <see cref="Domain.Tools.ToolUsage"/> telemetry and feeding results back into the
/// conversation. Extracted verbatim from <see cref="ExecutionOrchestrator"/> (R4.1).
/// </summary>
internal sealed class ChatToolDispatcher
{
    private readonly ILogger _logger;

    internal ChatToolDispatcher(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Handles a single iteration when the LLM emits native function calls.
    /// Logs the detected tool calls, appends the chat response to the history, and dispatches tool execution.
    /// </summary>
    internal async System.Threading.Tasks.Task HandleNativeFunctionCallsAsync(
        List<FunctionCallContent> functionCalls,
        ToolCallDispatchContext ctx,
        long elapsedMs,
        CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            var toolNames = string.Join(", ", functionCalls.Select(fc => fc.Name));
            ExecutionLog.LogChatClientToolCalls(_logger, ctx.Agent.Role, ctx.Iteration + 1, elapsedMs,
                toolNames);
        }
        ctx.Messages.AddRange(ctx.ChatResponse.Messages);
        await ProcessFunctionCallsAsync(
            functionCalls,
            new FunctionCallContext(ctx.AvailableTools, ctx.Messages, ctx.ToolsUsed, ctx.Agent, ctx.Task),
            ctx.Iteration, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Handles a single iteration when the LLM emitted [TOOL_CALL] text blocks instead of native function calls.
    /// Executes the parsed tool calls and feeds the results back into the conversation.
    /// </summary>
    internal async System.Threading.Tasks.Task HandleTextFallbackToolCallsAsync(
        List<ParsedToolCall> textToolCalls,
        ToolCallDispatchContext ctx,
        CancellationToken cancellationToken)
    {
        ExecutionLog.LogChatClientTextToolCallFallback(_logger, ctx.Agent.Role, ctx.Iteration + 1, textToolCalls.Count);

        var toolsByName = ctx.AvailableTools.ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);
        var toolResultsBuilder = new StringBuilder();

        foreach (var call in textToolCalls)
        {
            if (!toolsByName.TryGetValue(call.ToolName, out var tool))
            {
                ExecutionLog.LogToolNotFound(_logger, call.ToolName, ctx.Agent.Role);
                toolResultsBuilder.AppendLine(FormattableString.Invariant($"[Tool '{call.ToolName}' not found]"));
                continue;
            }

            await ExecuteTextFallbackToolAsync(
                tool, call, toolResultsBuilder, ctx.ToolsUsed, ctx.Agent, ctx.Task, cancellationToken).ConfigureAwait(false);
        }

        // Feed tool results back into the conversation for the next iteration
        ctx.Messages.AddRange(ctx.ChatResponse.Messages);
        ctx.Messages.Add(new ChatMessage(ChatRole.User,
            $"{PromptDefaults.ToolResultsPrefix}{toolResultsBuilder}\n\nContinue working on the task. You may call additional tools if needed, or provide your final answer when the task is fully complete."));
    }

    /// <summary>
    /// Executes a single [TOOL_CALL]-parsed tool and records success/failure state.
    /// Keeps the try/catch isolated so that the loop in <see cref="HandleTextFallbackToolCallsAsync"/> stays shallow.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Tool-execution fault barrier: any tool failure is recorded as a failed ToolUsage and fed back to the LLM as an error result so one tool cannot crash the agent loop.")]
    private async System.Threading.Tasks.Task ExecuteTextFallbackToolAsync(
        Domain.Tools.IBaseTool tool,
        ParsedToolCall call,
        StringBuilder toolResultsBuilder,
        List<Domain.Tools.ToolUsage> toolsUsed,
        DomainAgent agent,
        CrewTask task,
        CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            var toolArgs = ToolCallFormatting.FormatToolArgs(call.Parameters);
            ExecutionLog.LogExecutingLegacyTool(_logger, call.ToolName, toolArgs);
        }
        var request = new Domain.Tools.Protocol.ToolCallRequest(call.ToolName, call.Parameters);
        var toolStartTime = DateTime.UtcNow;

        try
        {
            var result = await tool.CallAsync(request, cancellationToken).ConfigureAwait(false);
            var toolDuration = DateTime.UtcNow - toolStartTime;
            var resultText = result.Success
                ? ToolCallFormatting.FormatResult(result.Result)
                : $"Error: {result.Error}";

            // Truncate verbose tool results to bound context window growth
            var contextResultText = ConversationPolicy.TruncateToolResult(resultText, AgentDefaults.ResolveMaxToolResultLength(call.ToolName));

            if (_logger.IsEnabled(LogLevel.Information))
            {
                var truncatedResult = ToolCallFormatting.Truncate(resultText, LoggingDefaults.MaxToolResultLogLength);
                ExecutionLog.LogLegacyToolResult(_logger, call.ToolName, toolDuration.TotalMilliseconds, result.Success ? "OK" : "FAIL", truncatedResult);
            }
            toolResultsBuilder.AppendLine(FormattableString.Invariant($"[Tool {call.ToolName} result]: {contextResultText}"));

            toolsUsed.Add(Domain.Tools.ToolUsage.CreateSuccess(
                new ToolCallIdentity(Guid.NewGuid().ToString(), call.ToolName, agent.Id.ToString(), task.Id.ToString()),
                toolDuration,
                Domain.Memory.ValueObjects.ToolUsageMetadata.CreateBuilder()
                    .AddInput(System.Text.Json.JsonSerializer.Serialize(call.Parameters))
                    .AddOutput(resultText)
                    .Build()));
        }
        catch (Exception ex)
        {
            var toolDuration = DateTime.UtcNow - toolStartTime;
            ExecutionLog.LogTaskExecutionError(_logger, ex, call.ToolName, agent.Id);
            toolResultsBuilder.AppendLine(FormattableString.Invariant($"[Tool {call.ToolName} error]: {ex.Message}"));

            toolsUsed.Add(Domain.Tools.ToolUsage.CreateFailure(
                new ToolCallIdentity(Guid.NewGuid().ToString(), call.ToolName, agent.Id.ToString(), task.Id.ToString()),
                toolDuration, ex.Message));
        }
    }

    /// <summary>
    /// Processes function call results from the chat response, executes the corresponding tools,
    /// and appends tool results to the conversation messages.
    /// </summary>
    private async System.Threading.Tasks.Task ProcessFunctionCallsAsync(
        List<FunctionCallContent> functionCalls,
        FunctionCallContext callContext,
        int iteration,
        CancellationToken cancellationToken)
    {
        foreach (var fc in functionCalls)
        {
            ExecutionLog.LogExecutingTool(_logger, fc.Name, iteration + 1);

            var tool = callContext.AvailableTools.FirstOrDefault(t => t.Name == fc.Name);
            if (tool is null)
            {
                HandleMissingTool(fc, callContext.Agent, callContext.Messages);
                continue;
            }

            await ExecuteAndRecordToolCallAsync(tool, fc, callContext.Messages, callContext.ToolsUsed, callContext.Agent, callContext.Task, cancellationToken).ConfigureAwait(false);
        }
    }

    private void HandleMissingTool(FunctionCallContent fc, DomainAgent agent, List<ChatMessage> messages)
    {
        ExecutionLog.LogToolNotFound(_logger, fc.Name, agent.Role);
        messages.Add(BuildMissingToolMessage(fc.CallId, fc.Name));
    }

    // OpenAI-compat strict providers (DeepSeek included) reject the next request with HTTP 400
    // when an assistant message carrying tool_calls is not followed by a tool message bearing
    // the matching tool_call_id. The previous shape — ChatMessage(ChatRole.Tool, "Error: ...")
    // — produced a Tool message without any FunctionResultContent, so the call_id was lost and
    // the provider could not pair it with the assistant's tool_calls. See experiment 07 #10.
    internal static ChatMessage BuildMissingToolMessage(string callId, string toolName)
    {
        return new ChatMessage(ChatRole.Tool,
            [new FunctionResultContent(callId, $"Error: Tool '{toolName}' not found")]);
    }

    private static Dictionary<string, object?> ExtractArguments(FunctionCallContent fc)
    {
        if (fc.Arguments is null)
            return [];

        return UnwrapCallEnvelope(fc.Arguments.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
    }

    private static readonly HashSet<string> s_envelopeKeys =
        new(StringComparer.OrdinalIgnoreCase) { "type", "function", "name", "parameters", "arguments" };

    // Small models put the whole tool-call envelope into the arguments -- llama3.2:1b
    // (measured 2026-09-11 on the README quickstart) answers a file_write call with
    // {"type":"function","function":"file_write","parameters":{"path":...,"content":...}}.
    // Passed through as-is, the tool saw no `path`, said so, and the model repeated the
    // exact same envelope until the circuit breaker tripped. When every key belongs to
    // that envelope and the payload is an object, the payload is the arguments.
    internal static Dictionary<string, object?> UnwrapCallEnvelope(Dictionary<string, object?> arguments)
    {
        if (arguments.Count == 0 || !arguments.Keys.All(s_envelopeKeys.Contains))
            return arguments;

        var payloadKey = arguments.Keys.FirstOrDefault(k => k.Equals("parameters", StringComparison.OrdinalIgnoreCase))
            ?? arguments.Keys.FirstOrDefault(k => k.Equals("arguments", StringComparison.OrdinalIgnoreCase));
        if (payloadKey is null)
            return arguments;

        switch (arguments[payloadKey])
        {
            case System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Object } element:
                return element.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
            case IDictionary<string, object?> dictionary:
                return new Dictionary<string, object?>(dictionary);
            case System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } text:
                return TryParseObject(text.GetString()) ?? arguments;
            case string text:
                return TryParseObject(text) ?? arguments;
            default:
                return arguments;
        }
    }

    private static Dictionary<string, object?>? TryParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                return null;
            return document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Tool-execution fault barrier: any tool failure is recorded as a failed ToolUsage and returned to the LLM as a FunctionResultContent error so one tool cannot crash the agent loop.")]
    private async System.Threading.Tasks.Task ExecuteAndRecordToolCallAsync(
        Domain.Tools.IBaseTool tool,
        FunctionCallContent fc,
        List<ChatMessage> messages,
        List<Domain.Tools.ToolUsage> toolsUsed,
        DomainAgent agent,
        CrewTask task,
        CancellationToken cancellationToken)
    {
        var parameters = ExtractArguments(fc);
        if (_logger.IsEnabled(LogLevel.Information))
        {
            var toolArgs = ToolCallFormatting.FormatToolArgs(parameters);
            ExecutionLog.LogToolCallStart(_logger, tool.Name, toolArgs);
        }

        // One execute_tool span per call the model made, paired with the model's own
        // call id (gen_ai.tool.call.id), child of the agent's invoke_agent span.
        using var toolActivity = Telemetry.OrkeonActivitySources.Tool.StartActivity(
            GenAiAttributes.SpanName(GenAiAttributes.OperationExecuteTool, tool.Name), System.Diagnostics.ActivityKind.Internal);
        toolActivity?.SetTag(GenAiAttributes.OperationName, GenAiAttributes.OperationExecuteTool);
        toolActivity?.SetTag(GenAiAttributes.ToolName, tool.Name);
        toolActivity?.SetTag(GenAiAttributes.ToolCallId, fc.CallId);
        toolActivity?.SetTag(GenAiAttributes.AgentName, agent.Role.ToString());

        var toolStartTime = DateTime.UtcNow;
        var request = new Domain.Tools.Protocol.ToolCallRequest(tool.Name, parameters);

        try
        {
            var result = await tool.CallAsync(request, cancellationToken).ConfigureAwait(false);
            var toolDuration = DateTime.UtcNow - toolStartTime;
            if (!result.Success)
            {
                toolActivity?.SetTag(GenAiAttributes.ErrorType, "tool_error");
                toolActivity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, result.Error);
            }

            var resultText = result.Success
                ? ToolCallFormatting.FormatResult(result.Result)
                : $"Error: {result.Error}";

            // Truncate verbose tool results to bound context window growth
            var contextResultText = ConversationPolicy.TruncateToolResult(resultText, AgentDefaults.ResolveMaxToolResultLength(tool.Name));

            if (_logger.IsEnabled(LogLevel.Information))
            {
                var truncatedResult = ToolCallFormatting.Truncate(resultText, LoggingDefaults.MaxToolResultLogLength);
                ExecutionLog.LogToolCallResult(_logger, tool.Name, toolDuration.TotalMilliseconds,
                    result.Success ? "OK" : "FAIL",
                    truncatedResult);
            }

            messages.Add(new ChatMessage(ChatRole.Tool,
                [new FunctionResultContent(fc.CallId, contextResultText)]));

            toolsUsed.Add(Domain.Tools.ToolUsage.CreateSuccess(
                new ToolCallIdentity(Guid.NewGuid().ToString(), tool.Name, agent.Id.ToString(), task.Id.ToString()),
                toolDuration,
                Domain.Memory.ValueObjects.ToolUsageMetadata.CreateBuilder()
                    .AddInput(System.Text.Json.JsonSerializer.Serialize(parameters))
                    .AddOutput(resultText)
                    .Build()));
        }
        catch (Exception ex)
        {
            var toolDuration = DateTime.UtcNow - toolStartTime;
            toolActivity?.SetTag(GenAiAttributes.ErrorType, ex.GetType().FullName);
            toolActivity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            ExecutionLog.LogToolCallResult(_logger, tool.Name, toolDuration.TotalMilliseconds, "ERROR", ex.Message);

            messages.Add(new ChatMessage(ChatRole.Tool,
                [new FunctionResultContent(fc.CallId, $"Error: {ex.Message}")]));

            toolsUsed.Add(Domain.Tools.ToolUsage.CreateFailure(
                new ToolCallIdentity(Guid.NewGuid().ToString(), tool.Name, agent.Id.ToString(), task.Id.ToString()),
                toolDuration, ex.Message));
        }
    }
}
