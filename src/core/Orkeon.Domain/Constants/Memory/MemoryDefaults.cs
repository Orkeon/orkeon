namespace Orkeon.Domain.Constants.Memory;

/// <summary>
/// Centralised memory constants used across the Orkeon platform.
/// Eliminates scattered magic integer literals and provides a single
/// place to tune default memory capacities, limits, and thresholds.
/// </summary>
public static class MemoryDefaults
{
    // ── Short / Long term capacities ────────────────────────────────────

    /// <summary>
    /// Default short-term memory capacity for an agent memory store (20 items).
    /// </summary>
    public const int ShortTermCapacity = 20;

    /// <summary>
    /// Default long-term memory capacity for an agent memory store (1 000 items).
    /// Also used as the default <c>MaxItems</c> in <c>MemoryConfig</c>.
    /// </summary>
    public const int LongTermCapacity = 1_000;

    // ── Search ────────────────────────────────────────────────────────────

    /// <summary>
    /// Default maximum number of results returned by memory/knowledge search (10).
    /// </summary>
    public const int DefaultSearchLimit = 10;

    // ── Importance ────────────────────────────────────────────────────────

    /// <summary>
    /// Default importance score assigned to a memory item (0.5 — medium importance).
    /// </summary>
    public const float DefaultImportance = 0.5f;

    // ── Application-level limits ──────────────────────────────────────────

    /// <summary>
    /// Default maximum number of short-term items exposed at application level (100).
    /// Used in <c>OrkeonApplicationOptions</c> and <c>CreateAgentRequest</c>.
    /// </summary>
    public const int DefaultMaxShortTermItems = 100;

    /// <summary>
    /// Default maximum number of short-term entries retained in a crew memory store (1 000).
    /// </summary>
    public const int DefaultMaxShortTermEntries = 1_000;

    /// <summary>
    /// Default maximum number of long-term entries retained in a crew memory store (10 000).
    /// </summary>
    public const int DefaultMaxLongTermEntries = 10_000;

    /// <summary>
    /// Default number of days memory entries are retained before automatic eviction (30 days).
    /// </summary>
    public const int DefaultRetentionDays = 30;

    // ── Provider ─────────────────────────────────────────────────────────

    /// <summary>
    /// Default memory provider name ("InMemory").
    /// </summary>
    public const string DefaultProvider = "InMemory";

    /// <summary>Default retention period for memory entries (30 days).</summary>
    public static readonly TimeSpan DefaultRetentionPeriod = TimeSpan.FromDays(30);

    /// <summary>Default metric retention window for workload tracking (5 min).</summary>
    public static readonly TimeSpan DefaultMetricRetentionPeriod = TimeSpan.FromMinutes(5);
}
