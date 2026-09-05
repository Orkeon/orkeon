using DomainAgent = Orkeon.Domain.Agent.Agent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Constants.Agent;
using Orkeon.Domain.Task;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// Multi-turn execution loop using IChatClient with native function calling.
/// Extracted verbatim from <see cref="ExecutionOrchestrator"/> (R4.1): iteration loop,
/// tool-call dispatch routing, circuit breaker wiring, soft-budget steering, cache-usage
/// accounting, empty-final-message retry and max-iteration exhaustion handling.
/// </summary>
internal sealed class ChatClientAgentLoop
{
    private readonly ILogger _logger;
    private readonly IChatClient _chatClient;
    private readonly LlmCallGate _llmGate;
    private readonly ChatOptionsComposer _optionsComposer;
    private readonly ChatToolDispatcher _toolDispatcher;
    private readonly Interfaces.Ports.ILlmUsageSink? _usageSink;

    internal ChatClientAgentLoop(
        ILogger logger,
        IChatClient chatClient,
        LlmCallGate llmGate,
        ChatOptionsComposer optionsComposer,
        ChatToolDispatcher toolDispatcher,
        Interfaces.Ports.ILlmUsageSink? usageSink = null)
    {
        _logger = logger;
        _chatClient = chatClient;
        _llmGate = llmGate;
        _optionsComposer = optionsComposer;
        _toolDispatcher = toolDispatcher;
        _usageSink = usageSink;
    }

    /// <summary>
    /// Tracks circuit breaker state between iterations without requiring ref parameters in async methods.
    /// </summary>
    private sealed class CircuitBreakerState
    {
        public string? LastErrorMessage { get; set; }
        public int ConsecutiveIdenticalErrors { get; set; }
    }

    /// <summary>
    /// Bundles the stable per-task chat-loop state (agent, conversation, options, budget, task id)
    /// reused across retry/exhaustion helpers. Tames long argument lists (S107).
    /// </summary>
    private sealed record RetryLoopContext(
        DomainAgent Agent,
        List<ChatMessage> Messages,
        ChatOptions Options,
        int MaxIter,
        object TaskId);

    /// <summary>
    /// Multi-turn execution loop using IChatClient with native function calling.
    /// Returns an <see cref="AgentLoopResult"/> with the final output, token count, and exit reason.
    /// </summary>
    internal async System.Threading.Tasks.Task<AgentLoopResult> ExecuteAsync(
        DomainAgent agent,
        CrewTask task,
        string systemPrompt,
        string userPrompt,
        List<Domain.Tools.ToolUsage> toolsUsed,
        int defaultMaxIterations,
        CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userPrompt)
        };

        var (options, availableTools) = await _optionsComposer.BuildChatOptionsAsync(agent, task, cancellationToken).ConfigureAwait(false);
        var maxIter = agent.MaxIterations > 0 ? agent.MaxIterations : defaultMaxIterations;
        var totalTokensUsed = 0;
        // Prompt/completion split accumulated from UsageDetails when the provider reports
        // it (R10.8). Tool-free retry turns only feed the total, so split <= total.
        var promptTokensTotal = 0;
        var completionTokensTotal = 0;
        long cacheHitTotal = 0;
        long cacheMissTotal = 0;
        var softBudgetSteeringInjected = false;

        // Circuit breaker state: detect repeated identical tool call errors
        var cbState = new CircuitBreakerState();

        for (int iteration = 0; iteration < maxIter; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!softBudgetSteeringInjected)
            {
                softBudgetSteeringInjected = TryInjectSoftBudgetSteering(agent, task, messages, totalTokensUsed);
            }

            ExecutionLog.LogChatClientIteration(_logger, agent.Role, iteration + 1, maxIter, messages.Count);

            // Acquire rate limit lease for the LLM call only — released before tool execution
            // to prevent deadlocks when tools (e.g. delegate_work) trigger nested agent executions.
            var iterSw = System.Diagnostics.Stopwatch.StartNew();
            ChatResponse chatResponse;
            var llmLease = await _llmGate.AcquireLlmLeaseAsync(agent, cancellationToken).ConfigureAwait(false);
            try
            {
                chatResponse = await _chatClient.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                llmLease?.Dispose();
            }
            iterSw.Stop();

            totalTokensUsed += (int)(chatResponse.Usage?.TotalTokenCount ?? 0);
            promptTokensTotal += (int)(chatResponse.Usage?.InputTokenCount ?? 0);
            completionTokensTotal += (int)(chatResponse.Usage?.OutputTokenCount ?? 0);
            AccumulateCacheUsage(chatResponse, ref cacheHitTotal, ref cacheMissTotal, agent.Role);

            ReportUsageToSink(agent, chatResponse);

            var dispatch = await TryDispatchToolCallsAsync(
                chatResponse,
                new ToolCallDispatchContext(chatResponse, messages, availableTools, toolsUsed, agent, task, iteration),
                iterSw.ElapsedMilliseconds, totalTokensUsed, cbState, cancellationToken).ConfigureAwait(false);
            cbState = dispatch.CircuitBreaker;

            if (dispatch.Handled)
            {
                if (dispatch.Terminal is not null)
                    return dispatch.Terminal with
                    {
                        PromptTokens = promptTokensTotal,
                        CompletionTokens = completionTokensTotal,
                        CacheHitTokens = cacheHitTotal,
                        CacheMissTokens = cacheMissTotal,
                    };
                continue;
            }

            var responseText = chatResponse.Text ?? string.Empty;

            // Empty-final-message retry: MiniMax (and some other providers) occasionally emit
            // tool_calls across several iterations and then terminate with no assistant text.
            // The deliverable resolver then persists an empty file. Force one retry with
            // tool_choice disabled so the model must produce text.
            if (string.IsNullOrWhiteSpace(responseText) && iteration > 0)
            {
                var (result, _) = await HandleEmptyResponseAsync(
                    new RetryLoopContext(agent, messages, options, maxIter, task.Id),
                    iteration, iterSw.ElapsedMilliseconds,
                    totalTokensUsed, cancellationToken).ConfigureAwait(false);
                return result with
                {
                    PromptTokens = promptTokensTotal,
                    CompletionTokens = completionTokensTotal,
                    CacheHitTokens = cacheHitTotal,
                    CacheMissTokens = cacheMissTotal,
                };
            }

            ExecutionLog.LogChatClientFinalResponse(_logger, agent.Role, iteration + 1, iterSw.ElapsedMilliseconds);
            return new AgentLoopResult(responseText, totalTokensUsed,
                AgentExitReason.Completed, IterationsUsed: iteration + 1)
            {
                PromptTokens = promptTokensTotal,
                CompletionTokens = completionTokensTotal,
                CacheHitTokens = cacheHitTotal,
                CacheMissTokens = cacheMissTotal,
            };
        }

        var maxIterResult = await HandleMaxIterExhaustedAsync(
            agent, messages, options, maxIter, task.Id, totalTokensUsed, cancellationToken).ConfigureAwait(false);
        return maxIterResult with
        {
            PromptTokens = promptTokensTotal,
            CompletionTokens = completionTokensTotal,
            CacheHitTokens = cacheHitTotal,
            CacheMissTokens = cacheMissTotal,
        };
    }

    /// <summary>
    /// One-shot soft-budget steering: when cumulative tokens cross the configured threshold,
    /// inject a system message so the agent pivots toward summary tools and stops over-fetching
    /// context. Injected once per task. Returns whether the steering message was injected.
    /// </summary>
    private bool TryInjectSoftBudgetSteering(
        DomainAgent agent, CrewTask task, List<ChatMessage> messages, int totalTokensUsed)
    {
        if (AgentDefaults.SoftTokenBudget <= 0 || totalTokensUsed < AgentDefaults.SoftTokenBudget)
            return false;

        messages.Add(new ChatMessage(ChatRole.System, AgentDefaults.SoftTokenBudgetSteeringMessage));
        if (_logger.IsEnabled(LogLevel.Information))
        {
            var taskId = task.Id.ToString();
            ExecutionLog.LogSoftBudgetSteering(_logger, agent.Role, taskId, totalTokensUsed, AgentDefaults.SoftTokenBudget);
        }
        return true;
    }

    /// <summary>
    /// Forwards one LLM call's usage to the optional <see cref="Interfaces.Ports.ILlmUsageSink"/>.
    /// This is the one place the YAML/agent path actually sees per-call usage — without this
    /// Record, ILlmUsageSink only ever heard from the scripting facade, and an observed crew
    /// run reported zero tokens no matter what it spent.
    /// </summary>
    private void ReportUsageToSink(DomainAgent agent, ChatResponse chatResponse)
    {
        var usage = chatResponse.Usage;
        if (_usageSink is null || usage is null)
            return;

        var counts = usage.AdditionalCounts;
        long? callHit = counts is not null
            && counts.TryGetValue(Application.Common.DTOs.LlmUsageMetadataKeys.CacheHitTokens, out var hitCount)
                ? hitCount : null;
        long? callMiss = counts is not null
            && counts.TryGetValue(Application.Common.DTOs.LlmUsageMetadataKeys.CacheMissTokens, out var missCount)
                ? missCount : null;

        _usageSink.Record(new Interfaces.Ports.CostUsageEvent
        {
            AgentId = agent.Role,
            Model = chatResponse.ModelId ?? string.Empty,
            PromptTokens = (int)(usage.InputTokenCount ?? 0),
            CompletionTokens = (int)(usage.OutputTokenCount ?? 0),
            // A partition of PromptTokens — the sink must never add these to totals.
            CacheHitTokens = callHit,
            CacheMissTokens = callMiss,
        });
    }

    /// <summary>
    /// Outcome of dispatching a chat response's tool calls. <see cref="Handled"/> is true
    /// when the response contained native function calls or text-fallback tool calls; in
    /// that case a non-null <see cref="Terminal"/> ends the loop and a null one means
    /// "continue to the next iteration". When <see cref="Handled"/> is false the caller
    /// treats the response as a final assistant message.
    /// </summary>
    private readonly record struct ToolCallDispatchOutcome(
        bool Handled, AgentLoopResult? Terminal, CircuitBreakerState CircuitBreaker);

    /// <summary>
    /// Routes a chat response to the native-function-call or text-fallback tool handler,
    /// extracting the branching that previously inflated <see cref="ExecuteAsync"/>'s
    /// cognitive complexity. Behavior is unchanged: native function calls take precedence,
    /// then text tool-call blocks (only when tools are available).
    /// </summary>
    private async System.Threading.Tasks.Task<ToolCallDispatchOutcome> TryDispatchToolCallsAsync(
        ChatResponse chatResponse,
        ToolCallDispatchContext context,
        long elapsedMs,
        int totalTokensUsed,
        CircuitBreakerState cbState,
        CancellationToken cancellationToken)
    {
        var functionCalls = chatResponse.Messages
            .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
            .ToList();

        if (functionCalls.Count > 0)
        {
            var (terminal, nextCb) = await TryHandleNativeFunctionCallsAsync(
                functionCalls, context, elapsedMs, totalTokensUsed, cbState, cancellationToken)
                .ConfigureAwait(false);
            return new ToolCallDispatchOutcome(true, terminal, nextCb);
        }

        var responseText = chatResponse.Text ?? string.Empty;
        var textToolCalls = ToolCallTextParser.ParseToolCallBlocks(responseText);

        if (textToolCalls.Count > 0 && context.AvailableTools.Count > 0)
        {
            var (terminal, nextCb) = await TryHandleTextFallbackCallsAsync(
                textToolCalls, context, totalTokensUsed, cbState, cancellationToken)
                .ConfigureAwait(false);
            return new ToolCallDispatchOutcome(true, terminal, nextCb);
        }

        return new ToolCallDispatchOutcome(false, null, cbState);
    }

    /// <summary>
    /// Reads DeepSeek-style prompt cache hit/miss counts from <see cref="ChatResponse.Usage"/>'s
    /// <see cref="UsageDetails.AdditionalCounts"/> bag (populated by <c>LlmProviderToChatClientAdapter</c>),
    /// accumulates them into the loop-scoped totals, and logs a warning when the invariant
    /// <c>hit + miss == prompt_tokens</c> is violated. Friction #5 lets operators pilot the cache
    /// instead of running blind.
    /// </summary>
    private void AccumulateCacheUsage(
        ChatResponse chatResponse,
        ref long cacheHitTotal,
        ref long cacheMissTotal,
        string agentRole)
    {
        var counts = chatResponse.Usage?.AdditionalCounts;
        if (counts is null) return;

        long iterHit = 0, iterMiss = 0;
        if (counts.TryGetValue(Application.Common.DTOs.LlmUsageMetadataKeys.CacheHitTokens, out var hit))
            iterHit = hit;
        if (counts.TryGetValue(Application.Common.DTOs.LlmUsageMetadataKeys.CacheMissTokens, out var miss))
            iterMiss = miss;

        cacheHitTotal += iterHit;
        cacheMissTotal += iterMiss;

        var inputTokens = chatResponse.Usage?.InputTokenCount ?? 0;
        if (inputTokens > 0 && (iterHit + iterMiss) > 0 && iterHit + iterMiss != inputTokens)
        {
            ExecutionLog.LogCacheTokenInvariantViolated(_logger, agentRole, iterHit, iterMiss, inputTokens);
        }
    }

    /// <summary>
    /// Dispatches native function calls, checks the circuit breaker, and trims history.
    /// Returns (terminal result, updated circuit breaker state).
    /// Terminal result is non-null when the circuit breaker trips.
    /// </summary>
    private async System.Threading.Tasks.Task<(AgentLoopResult? Terminal, CircuitBreakerState NextState)> TryHandleNativeFunctionCallsAsync(
        List<FunctionCallContent> functionCalls,
        ToolCallDispatchContext dispatch,
        long elapsedMs,
        int totalTokensUsed,
        CircuitBreakerState cbState,
        CancellationToken cancellationToken)
    {
        await _toolDispatcher.HandleNativeFunctionCallsAsync(
            functionCalls, dispatch, elapsedMs, cancellationToken).ConfigureAwait(false);

        var messages = dispatch.Messages;
        var newError = ConversationPolicy.ExtractLastToolError((IReadOnlyList<ChatMessage>)messages);
        var lastError = cbState.LastErrorMessage;
        var count = cbState.ConsecutiveIdenticalErrors;
        if (ConversationPolicy.EvaluateCircuitBreaker(newError, ref lastError, ref count))
        {
            ExecutionLog.LogCircuitBreakerTripped(_logger, dispatch.Agent.Role, count, newError!);
            var partial = ConversationPolicy.ExtractLastAssistantText(messages);
            return (ConversationPolicy.BuildCircuitBreakerResult(count, newError!, totalTokensUsed, dispatch.Iteration, partial), cbState);
        }

        cbState.LastErrorMessage = lastError;
        cbState.ConsecutiveIdenticalErrors = count;
        ConversationPolicy.TrimConversationHistory(messages, AgentDefaults.MaxContextMessages);
        return (null, cbState);
    }

    /// <summary>
    /// Dispatches text-fallback [TOOL_CALL] blocks, checks the circuit breaker, and trims history.
    /// Returns (terminal result, updated circuit breaker state).
    /// Terminal result is non-null when the circuit breaker trips.
    /// </summary>
    private async System.Threading.Tasks.Task<(AgentLoopResult? Terminal, CircuitBreakerState NextState)> TryHandleTextFallbackCallsAsync(
        List<ParsedToolCall> textToolCalls,
        ToolCallDispatchContext dispatch,
        int totalTokensUsed,
        CircuitBreakerState cbState,
        CancellationToken cancellationToken)
    {
        await _toolDispatcher.HandleTextFallbackToolCallsAsync(
            textToolCalls, dispatch, cancellationToken).ConfigureAwait(false);

        var messages = dispatch.Messages;
        var newError = ConversationPolicy.ExtractLastToolError((IReadOnlyList<ChatMessage>)messages);
        var lastError = cbState.LastErrorMessage;
        var count = cbState.ConsecutiveIdenticalErrors;
        if (ConversationPolicy.EvaluateCircuitBreaker(newError, ref lastError, ref count))
        {
            ExecutionLog.LogCircuitBreakerTripped(_logger, dispatch.Agent.Role, count, newError!);
            var partial = ConversationPolicy.ExtractLastAssistantText(messages);
            return (ConversationPolicy.BuildCircuitBreakerResult(count, newError!, totalTokensUsed, dispatch.Iteration, partial), cbState);
        }

        cbState.LastErrorMessage = lastError;
        cbState.ConsecutiveIdenticalErrors = count;
        ConversationPolicy.TrimConversationHistory(messages, AgentDefaults.MaxContextMessages);
        return (null, cbState);
    }

    /// <summary>
    /// Handles an empty assistant response mid-loop by retrying once with tool_choice disabled.
    /// Returns (result, extra tokens consumed by the retry).
    /// </summary>
    private async System.Threading.Tasks.Task<(AgentLoopResult Result, int ExtraTokens)> HandleEmptyResponseAsync(
        RetryLoopContext loop,
        int iteration,
        long elapsedMs,
        int totalTokensUsed,
        CancellationToken cancellationToken)
    {
        ExecutionLog.LogEmptyFinalMessageRetrying(_logger, loop.Agent.Role, iteration + 1);

        var retryText = await RetryWithoutToolsAsync(loop.Agent, loop.Messages, loop.Options, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(retryText.Text))
        {
            ExecutionLog.LogChatClientFinalResponse(_logger, loop.Agent.Role, iteration + 1, elapsedMs);
            return (new AgentLoopResult(retryText.Text, totalTokensUsed + retryText.Tokens,
                AgentExitReason.Completed, IterationsUsed: iteration + 2), retryText.Tokens);
        }

        ExecutionLog.LogMaxIterationsReached(_logger, loop.MaxIter, loop.TaskId);
        return (new AgentLoopResult(string.Empty, totalTokensUsed + retryText.Tokens,
            AgentExitReason.Completed, IterationsUsed: iteration + 2,
            LastError: "empty_final_message_after_retry"), retryText.Tokens);
    }

    /// <summary>
    /// Handles the post-loop path when all iterations were consumed by tool calls with no final text.
    /// Gives the model one last chance with tool_choice=none.
    /// </summary>
    private async System.Threading.Tasks.Task<AgentLoopResult> HandleMaxIterExhaustedAsync(
        DomainAgent agent,
        List<ChatMessage> messages,
        ChatOptions options,
        int maxIter,
        object taskId,
        int totalTokensUsed,
        CancellationToken cancellationToken)
    {
        // MaxIter path: the loop exhausted its budget entirely on tool calls and never produced
        // a final text turn. Give the model one last chance with tool_choice=none so it can
        // synthesize the deliverable from the context it has already gathered.
        var lastAssistantText = messages
            .LastOrDefault(m => m.Role == ChatRole.Assistant)?.Text ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(lastAssistantText))
        {
            ExecutionLog.LogMaxIterationsReached(_logger, maxIter, taskId);
            return new AgentLoopResult(lastAssistantText, totalTokensUsed,
                AgentExitReason.MaxIterationsReached, IterationsUsed: maxIter,
                LastError: "Agent did not produce a final answer within the allowed iterations");
        }

        ExecutionLog.LogEmptyFinalMessageRetrying(_logger, agent.Role, maxIter);

        var retry = await RetryWithoutToolsAsync(agent, messages, options, cancellationToken).ConfigureAwait(false);
        totalTokensUsed += retry.Tokens;

        if (!string.IsNullOrWhiteSpace(retry.Text))
        {
            return new AgentLoopResult(retry.Text, totalTokensUsed,
                AgentExitReason.Completed, IterationsUsed: maxIter);
        }

        ExecutionLog.LogMaxIterationsReached(_logger, maxIter, taskId);
        return new AgentLoopResult(string.Empty, totalTokensUsed,
            AgentExitReason.MaxIterationsReached, IterationsUsed: maxIter,
            LastError: "empty_final_message_after_retry");
    }

    /// <summary>
    /// Issues one follow-up LLM request with tool_choice disabled after a turn produced no
    /// assistant text. Returns the produced text (possibly empty) and the tokens consumed.
    ///
    /// When the first retry produces a response that is obviously not a final deliverable —
    /// e.g. DeepSeek thinking-mode models that mimic tool calls in their native DSML markup
    /// (<c>&lt;｜｜DSML｜｜tool_calls&gt;…</c>) instead of plain text — a second, more
    /// emphatic retry is issued. The escalation is bounded (max 2 wire requests) so the
    /// fallback always terminates.
    /// </summary>
    private async System.Threading.Tasks.Task<(string Text, int Tokens)> RetryWithoutToolsAsync(
        DomainAgent agent,
        List<ChatMessage> messages,
        ChatOptions options,
        CancellationToken cancellationToken)
    {
        messages.Add(new ChatMessage(ChatRole.User, FinalAnswerPolicy.BuildFinalAnswerNudge(escalated: false)));

        var retryOptions = FinalAnswerPolicy.CloneForToolFreeRetry(options);

        var (text, tokens) = await SendToolFreeRetryAsync(
            agent, messages, retryOptions, cancellationToken).ConfigureAwait(false);

        if (!FinalAnswerPolicy.IsToolCallMimicry(text)) return (text, tokens);

        // First retry was rejected (likely DeepSeek emitting DSML tool-call markup as text).
        // Append a second, stricter prompt and re-issue exactly once.
        ExecutionLog.LogToolCallMimicryDetected(_logger, agent.Role);
        messages.Add(new ChatMessage(ChatRole.User, FinalAnswerPolicy.BuildFinalAnswerNudge(escalated: true)));

        var (text2, tokens2) = await SendToolFreeRetryAsync(
            agent, messages, retryOptions, cancellationToken).ConfigureAwait(false);

        // If the model still mimics tool calls, drop the garbage and ship empty so the
        // validation gate fails loudly instead of writing DSML markup to disk as a deliverable.
        if (FinalAnswerPolicy.IsToolCallMimicry(text2))
        {
            ExecutionLog.LogToolCallMimicryPersisted(_logger, agent.Role);
            return (string.Empty, tokens + tokens2);
        }

        return (text2, tokens + tokens2);
    }

    /// <summary>
    /// Single tool-free request — extracted so the retry path can reuse the lease/options
    /// dance without duplication.
    /// </summary>
    private async System.Threading.Tasks.Task<(string Text, int Tokens)> SendToolFreeRetryAsync(
        DomainAgent agent,
        List<ChatMessage> messages,
        ChatOptions retryOptions,
        CancellationToken cancellationToken)
    {
        var lease = await _llmGate.AcquireLlmLeaseAsync(agent, cancellationToken).ConfigureAwait(false);
        ChatResponse response;
        try
        {
            response = await _chatClient.GetResponseAsync(messages, retryOptions, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lease?.Dispose();
        }

        var text = response.Text ?? string.Empty;
        var tokens = (int)(response.Usage?.TotalTokenCount ?? 0);
        messages.AddRange(response.Messages);
        return (text, tokens);
    }
}
