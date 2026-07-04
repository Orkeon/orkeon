using DomainAgent = Orkeon.Domain.Agent.Agent;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Constants.Orchestration;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Constants.Agent;
using Orkeon.Domain.Tools;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// Multi-turn execution loop using native structured tool calling via
/// <see cref="Domain.SharedKernel.ILlmProvider"/> and <see cref="Interfaces.LLM.IToolCallingStrategy"/>.
/// Extracted verbatim from <see cref="ExecutionOrchestrator"/> (R4.1). Falls back to
/// returning plain text when the LLM response contains no tool calls.
/// </summary>
internal sealed class NativeToolCallingAgentLoop
{
    private readonly ILogger _logger;
    private readonly Domain.SharedKernel.ILlmProvider _fullProvider;
    private readonly Interfaces.LLM.IToolCallingStrategy _toolCallingStrategy;
    private readonly IEnumerable<Domain.Tools.IBaseTool>? _registeredTools;
    private readonly LlmCallGate _llmGate;

    internal NativeToolCallingAgentLoop(
        ILogger logger,
        Domain.SharedKernel.ILlmProvider fullProvider,
        Interfaces.LLM.IToolCallingStrategy toolCallingStrategy,
        IEnumerable<Domain.Tools.IBaseTool>? registeredTools,
        LlmCallGate llmGate)
    {
        _logger = logger;
        _fullProvider = fullProvider;
        _toolCallingStrategy = toolCallingStrategy;
        _registeredTools = registeredTools;
        _llmGate = llmGate;
    }

    /// <summary>
    /// Multi-turn execution loop using native structured tool calling.
    /// Returns an <see cref="AgentLoopResult"/> with structured exit reason.
    /// </summary>
    internal async System.Threading.Tasks.Task<AgentLoopResult> ExecuteAsync(
        ExecutionInvocationContext invocation,
        int defaultMaxIterations,
        CancellationToken cancellationToken)
    {
        var agent = invocation.Agent;
        var task = invocation.Task;
        var toolsUsed = invocation.ToolsUsed;
        var sw = invocation.Stopwatch;
        var maxIter = agent.MaxIterations > 0 ? agent.MaxIterations : defaultMaxIterations;
        var parser = _toolCallingStrategy.Parser;

        var availableTools = ResolveNativeToolsForAgent(agent);
        var baseConfigForCascade = agent.LlmConfig ?? Domain.SharedKernel.ValueObjects.LlmConfig.Default();
        var nativeConfig = BuildNativeLlmConfig(invocation.SystemPrompt, availableTools, baseConfigForCascade);
        var config = Domain.SharedKernel.ValueObjects.LlmConfigResolver.Resolve(
            baseConfig: nativeConfig,
            taskOverride: task.LlmOverride,
            callOverride: null);
        var messages = new List<Domain.SharedKernel.ValueObjects.LlmMessage>
        {
            Domain.SharedKernel.ValueObjects.LlmMessage.System(invocation.SystemPrompt),
            Domain.SharedKernel.ValueObjects.LlmMessage.User(invocation.UserPrompt)
        };

        // Circuit breaker state
        string? lastErrorMessage = null;
        int consecutiveIdenticalErrors = 0;

        for (int iteration = 0; iteration < maxIter; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Acquire rate limit lease for the LLM call only — released before tool execution
            Domain.SharedKernel.ValueObjects.LlmResponse response;
            var llmLease = await _llmGate.AcquireLlmLeaseAsync(agent, cancellationToken).ConfigureAwait(false);
            try
            {
                response = await _fullProvider.ChatAsync(
                    messages.ToArray(), config, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                llmLease?.Dispose();
            }

            sw.Stop();
            ExecutionLog.LogLlmResponse(_logger, agent.Role, sw.ElapsedMilliseconds, response.Content.Length, response.Content);

            // Check for tool calls in the response
            if (string.IsNullOrEmpty(response.RawResponseBody))
            {
                // No raw body means no native tool calling support — return text
                return new AgentLoopResult(response.Content, 0,
                    AgentExitReason.Completed, IterationsUsed: iteration + 1);
            }

            using var doc = System.Text.Json.JsonDocument.Parse(response.RawResponseBody);
            var parsedCalls = parser.ParseToolCalls(doc.RootElement);

            if (parsedCalls.Count == 0)
            {
                // No tool calls — this is the final text answer
                return new AgentLoopResult(response.Content, 0,
                    AgentExitReason.Completed, IterationsUsed: iteration + 1);
            }

            AppendAssistantToolCallMessage(messages, response, doc.RootElement);

            // Execute each tool call
            ExecutionLog.LogLegacyToolCallsDetected(_logger, agent.Role, iteration + 1, parsedCalls.Count);
            foreach (var call in parsedCalls)
            {
                await ExecuteNativeToolCallAsync(
                    call, availableTools, messages, toolsUsed, agent, task, cancellationToken).ConfigureAwait(false);
            }

            // Circuit breaker check after tool execution
            var newError = ConversationPolicy.ExtractLastToolError((IReadOnlyList<Domain.SharedKernel.ValueObjects.LlmMessage>)messages);
            if (ConversationPolicy.EvaluateCircuitBreaker(newError, ref lastErrorMessage, ref consecutiveIdenticalErrors))
            {
                ExecutionLog.LogCircuitBreakerTripped(_logger, agent.Role, consecutiveIdenticalErrors, newError!);
                return ConversationPolicy.BuildCircuitBreakerResult(
                    consecutiveIdenticalErrors, newError!, totalTokensUsed: 0, iteration,
                    ConversationPolicy.ExtractLastAssistantText(messages));
            }
            ConversationPolicy.TrimConversationHistory(messages, AgentDefaults.MaxContextMessages);
            sw.Restart();
        }

        ExecutionLog.LogMaxIterationsReached(_logger, maxIter, task.Id);
        return new AgentLoopResult(
            PromptDefaults.MaxIterationsMessage,
            0,
            AgentExitReason.MaxIterationsReached,
            IterationsUsed: maxIter,
            LastError: "Agent did not produce a final answer within the allowed iterations");
    }

    /// <summary>
    /// Returns the dictionary of tools available to an agent for native tool calling (keyed by tool name).
    /// </summary>
    private Dictionary<string, Domain.Tools.IBaseTool> ResolveNativeToolsForAgent(DomainAgent agent)
    {
        var agentToolNames = agent.Tools.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Start with tools from the global DI registry
        var result = _registeredTools?
            .Where(t => agentToolNames.Contains(t.Name))
            .ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, Domain.Tools.IBaseTool>(StringComparer.OrdinalIgnoreCase);

        // Include agent-owned tools not in the registry (e.g. delegation tools)
        foreach (var tool in agent.Tools)
        {
            if (!result.ContainsKey(tool.Name))
                result[tool.Name] = tool;
        }

        return result;
    }

    /// <summary>
    /// Builds an <see cref="Domain.SharedKernel.ValueObjects.LlmConfig"/> seeded from the agent's
    /// base config (preserving its model, ResponseFormat, Thinking, etc.) and populated with
    /// the available tool schemas for the current turn.
    /// </summary>
    private static Domain.SharedKernel.ValueObjects.LlmConfig BuildNativeLlmConfig(
        string systemPrompt,
        Dictionary<string, Domain.Tools.IBaseTool> availableTools,
        Domain.SharedKernel.ValueObjects.LlmConfig agentBaseConfig)
    {
        var toolSchemas = availableTools.Values.Select(t => t.Schema).ToList();
        return agentBaseConfig with
        {
            SystemMessage = systemPrompt,
            Tools = toolSchemas.Count > 0 ? toolSchemas : null,
            ToolMode = Domain.Tools.Protocol.ToolCallMode.Auto
        };
    }

    /// <summary>
    /// Appends the assistant message carrying the raw <c>tool_calls</c> JSON from the LLM response
    /// so the conversation history can be replayed to the provider.
    /// </summary>
    private static void AppendAssistantToolCallMessage(
        List<Domain.SharedKernel.ValueObjects.LlmMessage> messages,
        Domain.SharedKernel.ValueObjects.LlmResponse response,
        System.Text.Json.JsonElement root)
    {
        var toolCallsJson = "";

        // Try Anthropic format first: content[].type == "tool_use"
        if (root.TryGetProperty("content", out var contentArray) &&
            contentArray.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var toolUseBlocks = new System.Collections.Generic.List<string>();
            foreach (var block in contentArray.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var t) && t.GetString() == "tool_use")
                    toolUseBlocks.Add(block.GetRawText());
            }
            if (toolUseBlocks.Count > 0)
                toolCallsJson = "[" + string.Join(",", toolUseBlocks) + "]";
        }

        // Fallback: OpenAI format choices[0].message.tool_calls
        if (string.IsNullOrEmpty(toolCallsJson) &&
            root.TryGetProperty("choices", out var choices) &&
            choices.GetArrayLength() > 0)
        {
            var firstChoice = choices[0];
            if (firstChoice.TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("tool_calls", out var tc))
            {
                toolCallsJson = tc.GetRawText();
            }
        }

        messages.Add(new Domain.SharedKernel.ValueObjects.LlmMessage
        {
            Role = "assistant",
            Content = response.Content ?? "",
            RawToolCalls = toolCallsJson,
            ReasoningContent = ExtractReasoningContentFromMetadata(response)
        });
    }

    /// <summary>
    /// Extracts the optional <c>reasoning_content</c> trace from the LLM response metadata.
    /// Thinking-mode providers (DeepSeek) require this to be replayed on the next turn —
    /// dropping it triggers an HTTP 400 on the very next request.
    /// </summary>
    private static string? ExtractReasoningContentFromMetadata(Domain.SharedKernel.ValueObjects.LlmResponse response)
    {
        if (response.Metadata is null) return null;
        if (!response.Metadata.TryGetValue("reasoning_content", out var raw)) return null;
        return raw as string;
    }

    /// <summary>
    /// Executes a single native tool call (parsed by <see cref="Interfaces.LLM.IToolCallParser"/>)
    /// and appends the tool result (or error) message to the conversation history.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Tool-execution fault barrier: any tool failure is recorded as a failed ToolUsage and returned to the LLM as a tool error message so one tool cannot crash the native tool-calling loop.")]
    private async System.Threading.Tasks.Task ExecuteNativeToolCallAsync(
        Interfaces.LLM.ParsedToolCall call,
        Dictionary<string, Domain.Tools.IBaseTool> availableTools,
        List<Domain.SharedKernel.ValueObjects.LlmMessage> messages,
        List<Domain.Tools.ToolUsage> toolsUsed,
        DomainAgent agent,
        Domain.Task.CrewTask task,
        CancellationToken cancellationToken)
    {
        if (!availableTools.TryGetValue(call.ToolName, out var tool))
        {
            ExecutionLog.LogUnknownToolRequested(_logger, call.ToolName);
            messages.Add(new Domain.SharedKernel.ValueObjects.LlmMessage
            {
                Role = "tool",
                Content = $"Error: Unknown tool: {call.ToolName}",
                ToolCallId = call.Id
            });
            return;
        }

        var toolStartTime = DateTime.UtcNow;
        try
        {
            var request = new Domain.Tools.Protocol.ToolCallRequest(call.ToolName, call.Arguments);
            var toolResponse = await tool.CallAsync(request, cancellationToken).ConfigureAwait(false);
            var toolDuration = DateTime.UtcNow - toolStartTime;
            var resultText = toolResponse.Success
                ? toolResponse.Result?.ToString() ?? ""
                : $"Error: {toolResponse.Error}";

            // Truncate verbose tool results to bound context window growth
            var contextResultText = ConversationPolicy.TruncateToolResult(resultText, AgentDefaults.ResolveMaxToolResultLength(call.ToolName));

            if (_logger.IsEnabled(LogLevel.Information))
            {
                var truncatedResult = ToolCallFormatting.Truncate(resultText, LoggingDefaults.MaxToolResultLogLength);
                ExecutionLog.LogLegacyToolResult(_logger, call.ToolName, toolDuration.TotalMilliseconds,
                    toolResponse.Success ? "OK" : "FAIL",
                    truncatedResult);
            }

            toolsUsed.Add(Domain.Tools.ToolUsage.CreateSuccess(
                new ToolCallIdentity(call.Id, call.ToolName, agent.Id.ToString(), task.Id.ToString()),
                toolDuration,
                Domain.Memory.ValueObjects.ToolUsageMetadata.CreateBuilder()
                    .AddInput(System.Text.Json.JsonSerializer.Serialize(call.Arguments))
                    .AddOutput(resultText)
                    .Build()));

            messages.Add(new Domain.SharedKernel.ValueObjects.LlmMessage
            {
                Role = "tool",
                Content = contextResultText,
                ToolCallId = call.Id
            });
        }
        catch (Exception ex)
        {
            var toolDuration = DateTime.UtcNow - toolStartTime;
            ExecutionLog.LogNativeToolExecutionFailed(_logger, ex, call.ToolName);

            toolsUsed.Add(Domain.Tools.ToolUsage.CreateFailure(
                new ToolCallIdentity(call.Id, call.ToolName, agent.Id.ToString(), task.Id.ToString()),
                toolDuration, ex.Message));

            messages.Add(new Domain.SharedKernel.ValueObjects.LlmMessage
            {
                Role = "tool",
                Content = $"Error: {ex.Message}",
                ToolCallId = call.Id
            });
        }
    }
}
