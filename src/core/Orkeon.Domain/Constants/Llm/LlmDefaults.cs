namespace Orkeon.Domain.Constants.Llm;

/// <summary>
/// Centralised LLM token constants used across the Orkeon platform.
/// Eliminates scattered magic integer literals and provides a single
/// place to tune default token counts.
/// </summary>
public static class LlmDefaults
{
    // ── Context Window ──────────────────────────────────────────────────

    /// <summary>
    /// Default context window size in tokens (4 096).
    /// Matches the canonical OpenAI GPT-3.5/GPT-4 context window value.
    /// </summary>
    public const int DefaultContextWindowTokens = 4_096;

    // ── Max Tokens ──────────────────────────────────────────────────────

    /// <summary>
    /// Default maximum number of tokens to generate in an LLM response (4 000).
    /// Slightly below the context window to leave room for the prompt.
    /// </summary>
    public const int DefaultMaxTokens = 4_000;

    // ── Temperature ─────────────────────────────────────────────────────

    /// <summary>
    /// Default sampling temperature for LLM inference (0.7).
    /// Balances creativity and determinism; range is 0.0 (deterministic) to 2.0 (very creative).
    /// </summary>
    public const double DefaultTemperature = 0.7;

    // ── Model Names ─────────────────────────────────────────────────────

    /// <summary>
    /// Default LLM model name used when no specific model is configured.
    /// </summary>
    public const string DefaultModelName = "gpt-4";

    /// <summary>
    /// Legacy LLM model name (GPT-3.5 Turbo) kept for backward compatibility.
    /// </summary>
    public const string LegacyModelName = "gpt-3.5-turbo";

    /// <summary>
    /// Default model used for planning operations (fast and cost-efficient).
    /// </summary>
    public const string DefaultPlanningModel = "gpt-4o-mini";
}
