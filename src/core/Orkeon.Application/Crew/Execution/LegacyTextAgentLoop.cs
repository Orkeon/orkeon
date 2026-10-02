using DomainAgent = Orkeon.Domain.Agent.Agent;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Constants.Orchestration;
using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Constants.Agent;
using Orkeon.Domain.Tools;
using System.Text;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// Multi-turn execution loop using legacy <see cref="IBasicLlmProvider"/> with
/// <c>[TOOL_CALL]</c> text block parsing. Extracted verbatim from
/// <see cref="ExecutionOrchestrator"/> (R4.1). After each LLM response, if [TOOL_CALL]
/// blocks are found, tools are executed and their results are appended to the
/// conversation before re-prompting the LLM for a final answer.
/// </summary>
internal sealed class LegacyTextAgentLoop
{
    private readonly ILogger _logger;
    private readonly IBasicLlmProvider _llmProvider;
    private readonly LlmCallGate _llmGate;
    private readonly IToolInvocationPipeline _toolInvocation;
    private readonly IEnumerable<IBaseTool>? _registeredTools;

    internal LegacyTextAgentLoop(
        ILogger logger,
        IBasicLlmProvider llmProvider,
        LlmCallGate llmGate,
        IToolInvocationPipeline toolInvocation,
        IEnumerable<IBaseTool>? registeredTools = null)
    {
        _logger = logger;
        _registeredTools = registeredTools;
        _llmProvider = llmProvider;
        _llmGate = llmGate;
        _toolInvocation = toolInvocation;
    }

    /// <summary>
    /// Multi-turn execution loop using legacy IBasicLlmProvider with [TOOL_CALL] text block parsing.
    /// Returns an <see cref="AgentLoopResult"/> with structured exit reason.
    /// </summary>
    internal async System.Threading.Tasks.Task<AgentLoopResult> ExecuteAsync(
        ExecutionInvocationContext invocation,
        int defaultMaxIterations,
        CancellationToken cancellationToken)
    {
        var agent = invocation.Agent;
        var toolsUsed = invocation.ToolsUsed;
        var sw = invocation.Stopwatch;
        var maxIter = agent.MaxIterations > 0 ? agent.MaxIterations : defaultMaxIterations;
        var conversationBuilder = BuildInitialConversation(invocation.SystemPrompt, invocation.UserPrompt);

        // Circuit breaker state
        string? lastErrorMessage = null;
        int consecutiveIdenticalErrors = 0;

        var task = invocation.Task;
        var legacyEffectiveConfig = Domain.SharedKernel.ValueObjects.LlmConfigResolver.Resolve(
            // An agent without a config names no model: the provider runs the call on its own (GAP-18).
            baseConfig: agent.LlmConfig ?? Domain.SharedKernel.ValueObjects.LlmConfig.OnProfile(),
            taskOverride: task.LlmOverride,
            callOverride: null);

        for (int iteration = 0; iteration < maxIter; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Acquire rate limit lease for the LLM call only — released before tool execution
            string response;
            var llmLease = await _llmGate.AcquireLlmLeaseAsync(agent, cancellationToken).ConfigureAwait(false);
            try
            {
                response = await _llmProvider.ChatAsync(conversationBuilder.ToString(), legacyEffectiveConfig, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                llmLease?.Dispose();
            }

            sw.Stop();
            ExecutionLog.LogLlmResponse(_logger, agent.Role, sw.ElapsedMilliseconds, response.Length, response);

            // Check for structured [TOOL_CALL] blocks
            var parsedCalls = ToolCallTextParser.ParseToolCallBlocks(response);
            if (parsedCalls.Count == 0)
            {
                // No structured tool calls — use single-turn fallback with simple tool name matching
                var (fallbackOutput, fallbackToolUsage) = await ProcessResponseLegacy(response, agent, task, cancellationToken).ConfigureAwait(false);
                toolsUsed.AddRange(fallbackToolUsage);

                // An empty text is not a final answer: it used to exit Completed here and the
                // task went green with no deliverable (STUDIO-12 C5a).
                if (string.IsNullOrWhiteSpace(fallbackOutput))
                {
                    ExecutionLog.LogEmptyFinalAnswer(_logger, agent.Role, task.Id);
                    return new AgentLoopResult(string.Empty, 0,
                        AgentExitReason.EmptyFinalAnswer, IterationsUsed: iteration + 1,
                        LastError: FinalAnswerPolicy.EmptyFinalAnswerReason);
                }

                ExecutionLog.LogLegacyFinalAnswer(_logger, agent.Role, iteration + 1);
                return new AgentLoopResult(fallbackOutput, 0,
                    AgentExitReason.Completed, IterationsUsed: iteration + 1);
            }

            // Execute structured tool calls and build tool results section
            ExecutionLog.LogLegacyToolCallsDetected(_logger, agent.Role, iteration + 1, parsedCalls.Count);
            var (toolResultText, extractedToolUsage) = await ProcessResponseLegacy(response, agent, task, cancellationToken).ConfigureAwait(false);
            toolsUsed.AddRange(extractedToolUsage);

            AppendToolResultsTurn(conversationBuilder, response, toolResultText);

            // Circuit breaker check after tool execution
            var newError = ConversationPolicy.ExtractLastToolErrorFromText(conversationBuilder.ToString());
            if (ConversationPolicy.EvaluateCircuitBreaker(newError, ref lastErrorMessage, ref consecutiveIdenticalErrors))
            {
                ExecutionLog.LogCircuitBreakerTripped(_logger, agent.Role, consecutiveIdenticalErrors, newError!);
                return new AgentLoopResult(
                    $"Agent stopped after {consecutiveIdenticalErrors} identical tool call failures. Error: {newError}",
                    0, AgentExitReason.CircuitBreakerTripped, IterationsUsed: iteration + 1,
                    LastError: newError);
            }

            // Trim the conversation builder to prevent unbounded growth.
            // This path is less precise (text-based) but still prevents runaway context size.
            if (conversationBuilder.Length > AgentDefaults.MaxContextMessages * 200)
            {
                TrimLegacyConversation(conversationBuilder, invocation.SystemPrompt, invocation.UserPrompt);
            }

            // Reset stopwatch for next iteration
            sw.Restart();
        }

        ExecutionLog.LogMaxIterationsReached(_logger, maxIter, invocation.Task.Id);
        return new AgentLoopResult(
            PromptDefaults.MaxIterationsMessage,
            0,
            AgentExitReason.MaxIterationsReached,
            IterationsUsed: maxIter,
            LastError: "Agent did not produce a final answer within the allowed iterations");
    }

    /// <summary>
    /// Builds the initial conversation prompt (system + user) for the legacy tool-calling loop.
    /// </summary>
    private static StringBuilder BuildInitialConversation(string systemPrompt, string userPrompt)
    {
        var conversationBuilder = new StringBuilder();
        conversationBuilder.AppendLine(systemPrompt);
        conversationBuilder.AppendLine();
        conversationBuilder.AppendLine(userPrompt);
        return conversationBuilder;
    }

    /// <summary>
    /// Trims a legacy (StringBuilder-based) conversation to prevent unbounded growth.
    /// Keeps the system/user prompt and the most recent portion of the conversation.
    /// </summary>
    private static void TrimLegacyConversation(StringBuilder conversationBuilder, string systemPrompt, string userPrompt)
    {
        var fullConversation = conversationBuilder.ToString();
        var headerLength = systemPrompt.Length + userPrompt.Length + 4; // account for newlines
        var maxKeepLength = AgentDefaults.MaxContextMessages * 200;

        // Keep the header (system + user prompt) and the tail of the conversation
        var tailLength = maxKeepLength - headerLength - 100; // 100 for trim notice
        if (tailLength <= 0) return;

        var tail = fullConversation.Length > tailLength
            ? fullConversation[^tailLength..]
            : fullConversation;

        conversationBuilder.Clear();
        conversationBuilder.AppendLine(systemPrompt);
        conversationBuilder.AppendLine();
        conversationBuilder.AppendLine(userPrompt);
        conversationBuilder.AppendLine();
        conversationBuilder.AppendLine("[Context trimmed: earlier messages removed. Focus on the most recent tool results and the original task.]");
        conversationBuilder.AppendLine();
        conversationBuilder.Append(tail);
    }

    /// <summary>
    /// Appends the assistant response and tool results as a new turn in the conversation history.
    /// </summary>
    private static void AppendToolResultsTurn(StringBuilder conversationBuilder, string assistantResponse, string toolResultText)
    {
        conversationBuilder.AppendLine();
        conversationBuilder.AppendLine("Assistant:");
        conversationBuilder.AppendLine(assistantResponse);
        conversationBuilder.AppendLine();
        conversationBuilder.AppendLine(PromptDefaults.ToolResultsPrefix);
        conversationBuilder.AppendLine(toolResultText);
        conversationBuilder.AppendLine();
        conversationBuilder.AppendLine("Continue working on the task. You may call additional tools if needed, or provide your final answer when the task is fully complete.");
    }

    private async System.Threading.Tasks.Task<(string output, List<Domain.Tools.ToolUsage> toolUsage)> ProcessResponseLegacy(
        string response,
        DomainAgent agent,
        Domain.Task.CrewTask task,
        CancellationToken cancellationToken)
    {
        var toolUsage = new List<Domain.Tools.ToolUsage>();

        // 1. Try structured [TOOL_CALL] parsing first
        var parsedCalls = ToolCallTextParser.ParseToolCallBlocks(response);
        if (parsedCalls.Count > 0)
        {
            return await ExecuteStructuredLegacyCallsAsync(
                response, parsedCalls, agent, task, toolUsage, cancellationToken).ConfigureAwait(false);
        }

        // 2. Fallback: old-style simple pattern matching (no structured blocks)
        return await ExecuteNameBasedFallbackAsync(
            response, agent, task, toolUsage, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the structured [TOOL_CALL] processing branch of <see cref="ProcessResponseLegacy"/>.
    /// Strips the [TOOL_CALL] blocks from the prose, executes each parsed call, and collects results.
    /// </summary>
    private async System.Threading.Tasks.Task<(string output, List<Domain.Tools.ToolUsage> toolUsage)> ExecuteStructuredLegacyCallsAsync(
        string response,
        List<ParsedToolCall> parsedCalls,
        DomainAgent agent,
        Domain.Task.CrewTask task,
        List<Domain.Tools.ToolUsage> toolUsage,
        CancellationToken cancellationToken)
    {
        var toolsByName = TaskToolbelt.Compose(agent, task, _registeredTools)
            .ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);
        var resultBuilder = new StringBuilder();

        AppendProseWithoutToolBlocks(resultBuilder, response, parsedCalls);

        foreach (var call in parsedCalls)
        {
            if (!toolsByName.TryGetValue(call.ToolName, out var tool))
            {
                ExecutionLog.LogToolNotFound(_logger, call.ToolName, agent.Role);
                resultBuilder.AppendLine(FormattableString.Invariant($"\n[Tool '{call.ToolName}' not found]"));
                continue;
            }

            await ExecuteLegacyStructuredCallAsync(
                tool, call, resultBuilder, toolUsage, agent, task, cancellationToken).ConfigureAwait(false);
        }

        return (resultBuilder.ToString(), toolUsage);
    }

    /// <summary>
    /// Removes [TOOL_CALL] blocks from the raw response and appends any remaining prose to <paramref name="resultBuilder"/>.
    /// </summary>
    private static void AppendProseWithoutToolBlocks(
        StringBuilder resultBuilder,
        string response,
        List<ParsedToolCall> parsedCalls)
    {
        var textWithoutCalls = response;
        foreach (var call in parsedCalls)
            textWithoutCalls = textWithoutCalls.Replace(call.RawBlock, "", StringComparison.Ordinal);

        var prose = textWithoutCalls.Trim();
        if (!string.IsNullOrEmpty(prose))
            resultBuilder.AppendLine(prose);
    }

    /// <summary>
    /// Executes a single parsed [TOOL_CALL] entry (success or failure) and appends its formatted output
    /// to <paramref name="resultBuilder"/>. Also records <see cref="Domain.Tools.ToolUsage"/> telemetry.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Tool-execution fault barrier: any tool failure is recorded as a failed ToolUsage and appended to the result text as an error so one tool cannot crash the agent loop.")]
    private async System.Threading.Tasks.Task ExecuteLegacyStructuredCallAsync(
        Domain.Tools.IBaseTool tool,
        ParsedToolCall call,
        StringBuilder resultBuilder,
        List<Domain.Tools.ToolUsage> toolUsage,
        DomainAgent agent,
        Domain.Task.CrewTask task,
        CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            var toolArgs = ToolCallFormatting.FormatToolArgs(call.Parameters);
            ExecutionLog.LogExecutingLegacyTool(_logger, call.ToolName, toolArgs);
        }

        var toolStartTime = DateTime.UtcNow;

        try
        {
            var outcome = await _toolInvocation.InvokeAsync(
                new ToolInvocation(tool, call.Parameters, ChatToolDispatcher.CallerOf(agent, task)), cancellationToken).ConfigureAwait(false);
            var toolDuration = DateTime.UtcNow - toolStartTime;
            if (outcome.Blocked)
            {
                resultBuilder.AppendLine(FormattableString.Invariant($"\n[Tool {call.ToolName} result]: {outcome.ConversationText}"));
                toolUsage.Add(Domain.Tools.ToolUsage.CreateFailure(
                    new ToolCallIdentity(Guid.NewGuid().ToString(), call.ToolName, agent.Id.ToString(), task.Id.ToString()),
                    toolDuration, outcome.ConversationText));
                return;
            }

            var result = outcome.Response!;
            var resultText = outcome.RawText;

            if (_logger.IsEnabled(LogLevel.Information))
            {
                var truncatedResult = ToolCallFormatting.Truncate(resultText, LoggingDefaults.MaxToolResultLogLength);
                ExecutionLog.LogLegacyToolResult(_logger, call.ToolName, toolDuration.TotalMilliseconds, result.Success ? "OK" : "FAIL", truncatedResult);
            }

            resultBuilder.AppendLine(FormattableString.Invariant($"\n[Tool {call.ToolName} result]: {outcome.ConversationText}"));

            toolUsage.Add(Domain.Tools.ToolUsage.CreateSuccess(
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
            resultBuilder.AppendLine(FormattableString.Invariant($"\n[Tool {call.ToolName} error]: {ex.Message}"));

            toolUsage.Add(Domain.Tools.ToolUsage.CreateFailure(
                new ToolCallIdentity(Guid.NewGuid().ToString(), call.ToolName, agent.Id.ToString(), task.Id.ToString()),
                toolDuration, ex.Message));
        }
    }

    /// <summary>
    /// Legacy fallback path: matches tool names by substring in the LLM response and executes them one by one,
    /// the whole response as the tool's <c>input</c>. Used when the LLM emits neither [TOOL_CALL] nor native
    /// function calls. Goes through the <see cref="IToolInvocationPipeline"/> like every other call (GAP-09).
    /// </summary>
    private async System.Threading.Tasks.Task<(string output, List<Domain.Tools.ToolUsage> toolUsage)> ExecuteNameBasedFallbackAsync(
        string response,
        DomainAgent agent,
        Domain.Task.CrewTask task,
        List<Domain.Tools.ToolUsage> toolUsage,
        CancellationToken cancellationToken)
    {
        var outputBuilder = new StringBuilder(response);
        var matchingTools = TaskToolbelt.Compose(agent, task, _registeredTools)
            .Where(tool => response.Contains(tool.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var tool in matchingTools)
        {
            var toolStartTime = DateTime.UtcNow;
            var outcome = await _toolInvocation.InvokeAsync(
                new ToolInvocation(tool, new Dictionary<string, object?> { ["input"] = response }, ChatToolDispatcher.CallerOf(agent, task)),
                cancellationToken).ConfigureAwait(false);

            if (outcome.Success)
            {
                toolUsage.Add(Domain.Tools.ToolUsage.CreateSuccess(new ToolCallIdentity(Guid.NewGuid().ToString(),
                    tool.Name, agent.Id.ToString(), task.Id.ToString()),
                    DateTime.UtcNow - toolStartTime, Domain.Memory.ValueObjects.ToolUsageMetadata.CreateBuilder()
                        .AddInput(response)
                        .AddOutput(outcome.RawText)
                        .Build()));

                outputBuilder.Append(FormattableString.Invariant($"\n\nTool {tool.Name} result: {outcome.ConversationText}"));
            }
        }

        return (outputBuilder.ToString(), toolUsage);
    }
}
