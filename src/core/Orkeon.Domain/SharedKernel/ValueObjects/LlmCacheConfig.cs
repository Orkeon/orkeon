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

    /// <summary>The lifetimes <see cref="Ttl"/> takes: Anthropic's two cache tiers.</summary>
    public static IReadOnlyList<string> Ttls { get; } = ["5m", "1h"];

    /// <summary>
    /// The explicit "no cache" request: no breakpoint. Set on an agent, a task override or a script
    /// agent, it replaces an inherited cache - the host default <c>Llm:Cache</c> included - and turns
    /// caching off, where a null cache inherits it.
    /// </summary>
    public static LlmCacheConfig Off { get; } = new();

    /// <summary>Caches the system prompt only — the common case and the cheapest win.</summary>
    public static LlmCacheConfig SystemPrompt() => new() { CacheSystemPrompt = true };

    /// <summary>
    /// The configuration a <c>cache</c> request of a crew file or a script describes: the one mapping
    /// the YAML <c>cache:</c> block and the script <c>cache({ ... })</c> option share, so both accept
    /// and refuse the same values.
    /// </summary>
    /// <param name="systemPrompt">Whether the system prompt is marked cacheable.</param>
    /// <param name="tools">Whether the tool definitions are marked cacheable.</param>
    /// <param name="ttl">The lifetime, one of <see cref="Ttls"/>; null keeps the vendor default.</param>
    /// <returns>
    /// The configuration, or null when it asks for no breakpoint: caching stays off unless it was
    /// actually requested.
    /// </returns>
    /// <exception cref="ArgumentException">The lifetime is not one of <see cref="Ttls"/>.</exception>
    public static LlmCacheConfig? Request(bool systemPrompt, bool tools, string? ttl)
    {
        if (ttl is not null && !Ttls.Contains(ttl, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"cache ttl must be {string.Join(" or ", Ttls)}, not '{ttl}'; leave it out for the vendor default.",
                nameof(ttl));
        }

        var config = new LlmCacheConfig { CacheSystemPrompt = systemPrompt, CacheTools = tools, Ttl = ttl };
        return config.RequestsAnyBreakpoint ? config : null;
    }
}
