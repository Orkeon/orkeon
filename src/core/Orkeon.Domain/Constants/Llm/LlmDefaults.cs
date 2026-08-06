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

    // ── Resilience ──────────────────────────────────────────────────────

    /// <summary>
    /// Default maximum number of retries on transient LLM API failures (10).
    /// Drives the buffered path's Polly retry policy and the streaming path's
    /// connect-phase retry loop; override per host with <c>Llm:MaxRetries</c>.
    /// </summary>
    public const int DefaultMaxRetries = 10;

    // ── Model Names ─────────────────────────────────────────────────────

    /// <summary>
    /// Default LLM model name used when no specific model is configured.
    /// GPT-5.6 Sol — the previous default, <c>gpt-4</c>, reaches end of life 2026-10-23
    /// and caps the context window at 8 192 tokens.
    /// </summary>
    public const string DefaultModelName = "gpt-5.6-sol";

    /// <summary>
    /// Legacy LLM model name (GPT-3.5 Turbo) kept for backward compatibility.
    /// </summary>
    public const string LegacyModelName = "gpt-3.5-turbo";

    /// <summary>
    /// Default model used for planning operations (fast and cost-efficient).
    /// </summary>
    public const string DefaultPlanningModel = "gpt-4o-mini";
}
