namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// Opt-in prompt-cache request, for the providers whose cache must be marked explicitly
/// rather than applied automatically.
/// </summary>
/// <remarks>
/// <para>
/// Most vendors cache prompt prefixes implicitly and simply report the hit. Anthropic does
/// not: without a <c>cache_control</c> breakpoint nothing is cached at all, so a long system
/// prompt or a large tool catalogue is re-billed at full price on every turn. Orkeon read
/// those metrics but never placed a breakpoint — the saving was unreachable
/// (audit gap G-17).
/// </para>
/// <para>
/// Caching is off by default and stays off unless asked for: a breakpoint changes what the
/// vendor stores and how the call is billed, which is not a decision to make on someone's
/// behalf. Providers that cache implicitly ignore this entirely.
/// </para>
/// </remarks>
public sealed record LlmCacheConfig
{
    /// <summary>
    /// The maximum number of cache breakpoints the Anthropic Messages API accepts in one
    /// request. Beyond this the API rejects the call, so the translation caps itself.
    /// </summary>
    public const int MaxBreakpoints = 4;

    /// <summary>Marks the system prompt as cacheable — usually the largest stable prefix.</summary>
    public bool CacheSystemPrompt { get; init; }

    /// <summary>
    /// Marks the tool definitions as cacheable. Worth it when the catalogue is large and
    /// stable across turns.
    /// </summary>
    public bool CacheTools { get; init; }

    /// <summary>
    /// Cache lifetime. <c>null</c> uses the vendor default (5 minutes on Anthropic);
    /// <c>"1h"</c> requests the extended tier, which is billed differently on write.
    /// </summary>
    public string? Ttl { get; init; }

    /// <summary>True when this configuration asks for at least one breakpoint.</summary>
    public bool RequestsAnyBreakpoint => CacheSystemPrompt || CacheTools;

    /// <summary>Caches the system prompt only — the common case and the cheapest win.</summary>
    public static LlmCacheConfig SystemPrompt() => new() { CacheSystemPrompt = true };
}
