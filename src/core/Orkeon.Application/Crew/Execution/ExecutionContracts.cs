using DomainAgent = Orkeon.Domain.Agent.Agent;
using Microsoft.Extensions.AI;
using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Task;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// Internal result carrying the output text, token count, structured exit reason,
/// iteration count, and optional last error from an execution loop.
/// </summary>
internal sealed record AgentLoopResult(
    string Output,
    int TokensUsed,
    AgentExitReason ExitReason = AgentExitReason.Completed,
    int IterationsUsed = 0,
    string? LastError = null)
{
    /// <summary>
    /// Cumulative prompt-side tokens across the loop, when the provider reports the
    /// prompt/completion split. 0 when the split is unavailable (R10.8 / MAT-004).
    /// </summary>
    public int PromptTokens { get; init; }
    /// <summary>Cumulative completion-side tokens across the loop (0 when the split is unavailable).</summary>
    public int CompletionTokens { get; init; }
    /// <summary>Cumulative DeepSeek <c>prompt_cache_hit_tokens</c> across the loop (Experiment 07 friction #5).</summary>
    public long CacheHitTokens { get; init; }
    /// <summary>Cumulative DeepSeek <c>prompt_cache_miss_tokens</c> across the loop.</summary>
    public long CacheMissTokens { get; init; }
}

/// <summary>
/// Bundles the invocation parameters required by the legacy/native execution loops.
/// Used to tame long argument lists (S107) for orchestration helpers.
/// </summary>
internal sealed record ExecutionInvocationContext(
    DomainAgent Agent,
    CrewTask Task,
    string SystemPrompt,
    string UserPrompt,
    SimpleExecutionContext? Context,
    List<Domain.Tools.ToolUsage> ToolsUsed,
    System.Diagnostics.Stopwatch Stopwatch);

/// <summary>
/// Bundles per-iteration dispatch state shared by both native and text-fallback tool call handlers.
/// </summary>
internal sealed record ToolCallDispatchContext(
    ChatResponse ChatResponse,
    List<ChatMessage> Messages,
    List<Domain.Tools.IBaseTool> AvailableTools,
    List<Domain.Tools.ToolUsage> ToolsUsed,
    DomainAgent Agent,
    CrewTask Task,
    int Iteration);

/// <summary>
/// Groups the conversation state needed during function call processing.
/// </summary>
internal sealed record FunctionCallContext(
    List<Domain.Tools.IBaseTool> AvailableTools,
    List<ChatMessage> Messages,
    List<Domain.Tools.ToolUsage> ToolsUsed,
    DomainAgent Agent,
    CrewTask Task);
