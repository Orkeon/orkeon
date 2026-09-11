using Microsoft.Extensions.Logging;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// Source-generated log messages shared by <see cref="ExecutionOrchestrator"/> and its
/// execution collaborators (loops, dispatcher, validation coordinator).
/// Extracted from <c>ExecutionOrchestrator</c> (R4.1) as static <c>[LoggerMessage]</c>
/// methods so every collaborator logs through the same
/// <c>ILogger&lt;ExecutionOrchestrator&gt;</c> instance — category, level, template,
/// EventName and EventId (a hash of the method name) are all preserved bit-for-bit.
/// </summary>
internal static partial class ExecutionLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Agent {Role}: tool-free retry returned tool-call mimicry; re-issuing with escalated nudge.")]
    internal static partial void LogToolCallMimicryDetected(ILogger logger, object role);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Agent {Role}: tool-free retry still produced tool-call mimicry after escalation; shipping empty so the validation gate flags the run.")]
    internal static partial void LogToolCallMimicryPersisted(ILogger logger, object role);

    [LoggerMessage(Level = LogLevel.Warning, Message = "LLM rate limit exceeded for agent {AgentRole} on provider {Provider}: {Reason}")]
    internal static partial void LogRateLimitExceeded(ILogger logger, object agentRole, string provider, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error executing task {TaskId} for agent {AgentId}")]
    internal static partial void LogTaskExecutionError(ILogger logger, Exception ex, object taskId, object agentId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Max iterations ({MaxIter}) reached for task {TaskId}")]
    internal static partial void LogMaxIterationsReached(ILogger logger, int maxIter, object taskId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Agent {AgentRole}: cache-token invariant violated — prompt_cache_hit_tokens ({CacheHit}) + prompt_cache_miss_tokens ({CacheMiss}) != prompt_tokens ({PromptTokens}). The provider's accounting is inconsistent; treat cache stats as approximate for this iteration.")]
    internal static partial void LogCacheTokenInvariantViolated(ILogger logger, string agentRole, long cacheHit, long cacheMiss, long promptTokens);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Soft token budget steering injected for agent [{AgentRole}] task={TaskId} (used={TokensUsed} threshold={Threshold})")]
    internal static partial void LogSoftBudgetSteering(ILogger logger, object agentRole, object taskId, int tokensUsed, int threshold);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Executing tool {ToolName} (iteration {Iteration})")]
    internal static partial void LogExecutingTool(ILogger logger, string toolName, int iteration);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tool {ToolName} not found for agent {AgentRole}")]
    internal static partial void LogToolNotFound(ILogger logger, string toolName, object agentRole);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error planning execution for task {TaskId}")]
    internal static partial void LogPlanningError(ILogger logger, Exception ex, object taskId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Output validation failed after {MaxRetries} retries for task {TaskId}: {Errors}")]
    internal static partial void LogOutputValidationExhausted(ILogger logger, int maxRetries, object taskId, string errors);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Output validation failed for task {TaskId} (retry {Retry}/{MaxRetries}): {Errors}")]
    internal static partial void LogOutputValidationRetry(ILogger logger, object taskId, int retry, int maxRetries, string errors);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Task {TaskId}: structured_output grammar conversion failed for {Path} ({Reason}). LLM will run without grammar constraint.")]
    internal static partial void LogGrammarConversionFailed(ILogger logger, string taskId, string path, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "structured_output.schema_path={Path} cannot be loaded: IFileSystemService is not registered in this host.")]
    internal static partial void LogSchemaPathUnsupported(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to read structured_output.schema_path={Path}: {Reason}")]
    internal static partial void LogSchemaPathReadFailed(ILogger logger, string path, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "LLM request started for agent [{AgentRole}] task={TaskId} provider={Provider}")]
    internal static partial void LogLlmRequestStart(ILogger logger, object agentRole, object taskId, string provider);

    [LoggerMessage(Level = LogLevel.Debug, Message = "LLM SYSTEM prompt for [{AgentRole}]:\n{SystemPrompt}")]
    internal static partial void LogLlmSystemPrompt(ILogger logger, object agentRole, string systemPrompt);

    [LoggerMessage(Level = LogLevel.Debug, Message = "LLM USER prompt for [{AgentRole}]:\n{UserPrompt}")]
    internal static partial void LogLlmUserPrompt(ILogger logger, object agentRole, string userPrompt);

    [LoggerMessage(Level = LogLevel.Information, Message = "LLM response for [{AgentRole}] in {ElapsedMs}ms ({ResponseLength} chars):\n{Response}")]
    internal static partial void LogLlmResponse(ILogger logger, object agentRole, long elapsedMs, int responseLength, string response);

    [LoggerMessage(Level = LogLevel.Debug, Message = "ChatClient iteration {Iteration}/{MaxIter} for [{AgentRole}] — {MessageCount} messages in context")]
    internal static partial void LogChatClientIteration(ILogger logger, object agentRole, int iteration, int maxIter, int messageCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "ChatClient iteration {Iteration} for [{AgentRole}] → tool calls in {ElapsedMs}ms: {ToolNames}")]
    internal static partial void LogChatClientToolCalls(ILogger logger, object agentRole, int iteration, long elapsedMs, string toolNames);

    [LoggerMessage(Level = LogLevel.Debug, Message = "ChatClient iteration {Iteration} for [{AgentRole}] → final response in {ElapsedMs}ms")]
    internal static partial void LogChatClientFinalResponse(ILogger logger, object agentRole, int iteration, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Agent [{AgentRole}] produced empty final message after {Iterations} iterations; retrying once with tool_choice=none")]
    internal static partial void LogEmptyFinalMessageRetrying(ILogger logger, object agentRole, int iterations);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ChatClient iteration {Iteration} for [{AgentRole}]: the answer is shaped like a tool call but none could be executed from it; asking the model to call the tool instead of describing the call")]
    internal static partial void LogToolCallShapedAnswerRetrying(ILogger logger, object agentRole, int iteration);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ChatClient iteration {Iteration} for [{AgentRole}]: no native function calls but found {TextToolCallCount} [TOOL_CALL] text blocks — falling back to text parsing")]
    internal static partial void LogChatClientTextToolCallFallback(ILogger logger, object agentRole, int iteration, int textToolCallCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tool call >> {ToolName}({ToolArgs})")]
    internal static partial void LogExecutingLegacyTool(ILogger logger, string toolName, string toolArgs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tool result << {ToolName} [{ElapsedMs:F0}ms {Status}]: {ResultPreview}")]
    internal static partial void LogLegacyToolResult(ILogger logger, string toolName, double elapsedMs, string status, string resultPreview);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tool call >> {ToolName}({ToolArgs})")]
    internal static partial void LogToolCallStart(ILogger logger, string toolName, string toolArgs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tool result << {ToolName} [{ElapsedMs:F0}ms {Status}]: {ResultPreview}")]
    internal static partial void LogToolCallResult(ILogger logger, string toolName, double elapsedMs, string status, string resultPreview);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Legacy provider: final answer from [{AgentRole}] at iteration {Iteration} (no more tool calls)")]
    internal static partial void LogLegacyFinalAnswer(ILogger logger, object agentRole, int iteration);

    [LoggerMessage(Level = LogLevel.Information, Message = "Legacy provider: [{AgentRole}] iteration {Iteration} detected {ToolCallCount} [TOOL_CALL] blocks — executing tools and re-prompting")]
    internal static partial void LogLegacyToolCallsDetected(ILogger logger, object agentRole, int iteration, int toolCallCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Agent [{AgentRole}] hit circuit breaker: {Count} consecutive identical tool call errors. Last error: {Error}. Aborting execution.")]
    internal static partial void LogCircuitBreakerTripped(ILogger logger, object agentRole, int count, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Task {TaskId}: deliverable not persisted by {Source} resolver at {Path} (reason: {Reason}).")]
    internal static partial void LogDeliverableNotPersisted(ILogger logger, string taskId, string source, string path, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Task {TaskId}: deliverable resolver {Source} threw unexpectedly for {Path}.")]
    internal static partial void LogDeliverableResolverError(ILogger logger, Exception ex, string taskId, string source, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unknown tool '{ToolName}' requested by LLM")]
    internal static partial void LogUnknownToolRequested(ILogger logger, string toolName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Tool execution failed for {ToolName}")]
    internal static partial void LogNativeToolExecutionFailed(ILogger logger, Exception ex, string toolName);
}
