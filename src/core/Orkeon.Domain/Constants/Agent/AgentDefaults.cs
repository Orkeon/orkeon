namespace Orkeon.Domain.Constants.Agent;

/// <summary>
/// Default values for agent configuration parameters.
/// Centralizes magic numbers used across the Agent aggregate and related services.
/// </summary>
public static class AgentDefaults
{
    /// <summary>
    /// Default placeholder value for an unassigned agent role or goal.
    /// </summary>
    public const string UnassignedValue = "Unassigned";

    /// <summary>
    /// Default maximum number of iterations for the agent execution loop.
    /// Prevents infinite loops when the LLM fails to produce a final answer.
    /// </summary>
    public const int MaxIterations = 15;

    /// <summary>
    /// Default maximum requests per minute for rate-limiting agent LLM calls.
    /// </summary>
    public const int MaxRequestsPerMinute = 10;

    /// <summary>
    /// Default maximum number of retry attempts on task execution failure.
    /// Aligned with the majority usage across the codebase (was 2, harmonised to 3).
    /// </summary>
    public const int MaxRetryLimit = 3;

    /// <summary>
    /// Default maximum number of in-memory agent memories retained.
    /// Older memories are trimmed when this limit is exceeded.
    /// </summary>
    public const int MaxMemories = 100;

    /// <summary>
    /// Maximum number of consecutive identical tool call errors before the circuit breaker trips.
    /// Prevents the agent from burning tokens re-trying the exact same failing action indefinitely.
    /// </summary>
    public const int MaxConsecutiveIdenticalErrors = 3;

    /// <summary>
    /// Maximum number of messages retained in the conversation context window.
    /// Prevents unbounded token growth during multi-turn agent execution loops.
    /// Set to 40 (roughly 20 assistant+tool pairs) to balance context richness with token cost.
    /// </summary>
    public const int MaxContextMessages = 40;

    /// <summary>
    /// Maximum number of characters allowed in a single tool result before truncation.
    /// Verbose tools (directory_read recursive, file_read of large files, web_scrape)
    /// can produce very large outputs that inflate the context window unnecessarily.
    /// </summary>
    public const int MaxToolResultLength = 4000;

    /// <summary>
    /// Per-tool overrides for <see cref="MaxToolResultLength"/>.
    /// Synthesizer-style flows read trusted, agent-produced deliverables from <c>/output/</c>
    /// (typically 5–20 KB Markdown). The default 4000-char cap forces re-read loops and
    /// pushes models into "fight the truncation" failure modes (cf. round 30 trpc).
    /// Listed tools get a wider cap; unlisted tools fall back to the default.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> MaxToolResultLengthOverrides =
        new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["file_read"] = 32_000,
        };

    /// <summary>
    /// Resolves the effective tool-result truncation cap for a given tool name.
    /// Returns the per-tool override when defined, otherwise <see cref="MaxToolResultLength"/>.
    /// </summary>
    public static int ResolveMaxToolResultLength(string toolName)
    {
        if (!string.IsNullOrEmpty(toolName)
            && MaxToolResultLengthOverrides.TryGetValue(toolName, out var overrideValue))
        {
            return overrideValue;
        }
        return MaxToolResultLength;
    }

    // ── Token budget steering ───────────────────────────────

    /// <summary>
    /// Soft token budget per task. When the agent's cumulative token usage crosses this
    /// threshold during the iteration loop, a one-shot steering system message is injected
    /// into the next prompt to push the agent away from heavy <c>symbol_detail</c> walks
    /// and toward summary tools (<c>complexity_report</c>, <c>codebase_summary</c>,
    /// <c>codebase_search</c> synthesis). Non-binding — the agent decides whether to
    /// follow the hint.
    /// <para>
    /// Set to <c>0</c> to disable steering globally. Tuned for round-33 observations:
    /// drizzle-orm at 299k tokens overshot, xstate at 145k stayed comfortable, so 200k
    /// is a reasonable trip point.
    /// </para>
    /// <para>
    /// <b>Disabled by default in this build (R34 regression).</b> Steering an analyst with
    /// a generic system message can leak into supervisor/verify tasks that expect a
    /// strict JSON-only output (the FINAL_SUMMARY contract). Re-enable only after the
    /// injection is gated on the task type (e.g. skip when the task declares a
    /// structured-output grammar).
    /// </para>
    /// </summary>
    public const int SoftTokenBudget = 0;

    /// <summary>
    /// Steering message injected once per task when <see cref="SoftTokenBudget"/> is crossed.
    /// Keep it short — it lands as an additional system prompt and competes for context.
    /// </summary>
    public const string SoftTokenBudgetSteeringMessage =
        "Token budget approaching its limit for this task. "
      + "Avoid further `symbol_detail` walks. Prefer summary tools "
      + "(`complexity_report`, `codebase_summary`, `codebase_search`) and "
      + "synthesize from already-fetched data when possible. "
      + "Produce the final deliverable as soon as you have enough material.";

    // ── Validation limits ───────────────────────────────────

    /// <summary>Maximum length of <see cref="Orkeon.Domain.Agent.ValueObjects.AgentRole"/> (2⁸).</summary>
    public const int AgentRoleMaxLength = 256;

    /// <summary>Maximum length of <see cref="Orkeon.Domain.Agent.ValueObjects.AgentGoal"/> (2¹¹).</summary>
    public const int AgentGoalMaxLength = 2_048;
}
