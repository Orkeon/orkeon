namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// Optional per-call "thinking mode" configuration for providers that distinguish a
/// reasoning pass from the visible response (DeepSeek V4, Anthropic extended thinking, …).
///
/// Surfaced by Experiment 07 friction #4 so a YAML crew can disable thinking on a
/// throwaway classification call or crank it to "max" for a planner agent without
/// changing C# wiring. Provider defaults apply when fields are left null.
/// </summary>
public sealed record LlmThinkingConfig
{
    /// <summary>Enables (<c>true</c>) or disables (<c>false</c>) the provider's thinking mode.</summary>
    public bool? Enabled { get; init; }

    /// <summary>
    /// Reasoning effort hint forwarded as the provider's <c>reasoning_effort</c> field.
    /// The levels are <see cref="Levels"/>: <c>"low"</c>, <c>"medium"</c>, <c>"high"</c>, <c>"xhigh"</c>,
    /// <c>"max"</c>. A provider declares the ones its field knows
    /// (<see cref="LlmProviderCapabilities.EffortLevels"/>) and sends <see cref="EffortFor"/> for the others.
    /// </summary>
    public string? Effort { get; init; }

    private static readonly string[] s_levels = ["low", "medium", "high", "xhigh", "max"];

    /// <summary>The effort levels a caller may ask for, lowest first.</summary>
    public static readonly IReadOnlyList<string> Levels = s_levels;

    /// <summary>
    /// The three levels a provider declares by default (<see cref="LlmProviderCapabilities.EffortLevels"/>):
    /// Ollama's <c>think</c>, and Gemini, Azure OpenAI and Mistral's <c>reasoning_effort</c> keep them.
    /// </summary>
    public static readonly IReadOnlyList<string> ThreeLevels = ["low", "medium", "high"];

    /// <summary>
    /// Returns <paramref name="effort"/> when it is exactly one of <see cref="Levels"/> (case-sensitive);
    /// throws <see cref="ArgumentException"/> otherwise, never falling back to a default level.
    /// </summary>
    public static string ValidateEffort(string effort)
    {
        if (effort is null || Array.IndexOf(s_levels, effort) < 0)
            throw new ArgumentException(
                $"Unknown thinking effort '{effort}': expected one of {string.Join(", ", s_levels)} (lower case).",
                nameof(effort));
        return effort;
    }

    /// <summary>
    /// The level a provider whose field knows <paramref name="known"/> sends for <paramref name="asked"/>:
    /// the level itself when known, else the highest known level below it (the lowest known one when none
    /// is). The provider that lowers a level reports it (LLM-02): an option is never silently changed.
    /// A value outside <see cref="Levels"/> (a vendor's own, such as OpenAI's <c>"none"</c>) is no level and is
    /// returned unchanged: the vendor judges it, rather than receiving a level the caller never asked for.
    /// </summary>
    public static string EffortFor(string asked, IReadOnlyList<string> known)
    {
        ArgumentNullException.ThrowIfNull(known);
        if (known.Contains(asked) || Array.IndexOf(s_levels, asked) < 0)
            return asked;

        var rank = Array.IndexOf(s_levels, asked);
        var below = known
            .Where(level => Array.IndexOf(s_levels, level) < rank)
            .OrderByDescending(level => Array.IndexOf(s_levels, level))
            .FirstOrDefault();
        return below ?? known.OrderBy(level => Array.IndexOf(s_levels, level)).First();
    }

    /// <summary>
    /// Hard token budget for the reasoning pass, for the providers whose API accepts one
    /// (Qwen's <c>thinking_budget</c>).
    /// </summary>
    /// <remarks>
    /// Deliberately <em>not</em> mapped to Anthropic: the <c>budget_tokens</c> shape found in
    /// many older sources is rejected with an HTTP 400 by the current Claude generation, which
    /// takes <c>thinking: {type: "adaptive"}</c> and an effort level instead. Providers that
    /// declare less than <see cref="ThinkingSupport.Budget"/> report the option rather than
    /// dropping it.
    /// </remarks>
    public int? BudgetTokens { get; init; }

    /// <summary>
    /// The effort left to System One (<c>effort: auto</c>, #439), in place of <see cref="Effort"/>, which stays null:
    /// the decision model picks the level of each task. Null when the effort is written, or left to the profile.
    /// </summary>
    public LlmAutoEffort? AutoEffort { get; init; }

    /// <summary>
    /// Whether System One may switch thinking off for a task (<c>enabled: auto</c>, #439), in place of
    /// <see cref="Enabled"/>, which stays null: the decision model's answers then hold <see cref="Off"/> beside the
    /// levels of <see cref="AutoEffort"/>, the written <see cref="Effort"/>, or <see cref="On"/> when neither is.
    /// </summary>
    public bool AutoEnabled { get; init; }

    /// <summary>The answer that switches thinking off for a task on <c>enabled: auto</c> (#439).</summary>
    public const string Off = "off";

    /// <summary>
    /// The answer that keeps thinking on, at the effort the agent would have had, for a task on <c>enabled: auto</c>
    /// with no effort written (#439).
    /// </summary>
    public const string On = "on";

    /// <summary>Gets whether something of this config is left to System One (<see cref="AutoEffort"/>, <see cref="AutoEnabled"/>).</summary>
    public bool HasAuto => AutoEffort is not null || AutoEnabled;

    /// <summary>
    /// The answers System One picks among for a task on this config (#439), cheapest first: <see cref="Off"/> on
    /// <c>enabled: auto</c>, then the levels <see cref="AutoEffort"/> allows among <paramref name="hostLevels"/>, else the
    /// written <see cref="Effort"/>, else <see cref="On"/> on <c>enabled: auto</c>. Empty when nothing is left to
    /// System One, or when its bounds leave no level.
    /// </summary>
    /// <param name="hostLevels">The levels the host allows, cheapest first.</param>
    /// <returns>The answers offered.</returns>
    public IReadOnlyList<string> AutoOptions(IReadOnlyList<string> hostLevels)
    {
        ArgumentNullException.ThrowIfNull(hostLevels);
        if (!HasAuto)
            return [];

        var levels = AutoEffort is { } auto ? auto.Allowed(hostLevels) : Effort is { } written ? [written] : [];
        if (!AutoEnabled)
            return levels;
        if (AutoEffort is not null && levels.Count == 0)
            return [];
        return [Off, .. levels.Count == 0 ? [On] : levels];
    }

    /// <summary>
    /// The thinking a task's calls run on once System One has settled what this config leaves to it (#439): with
    /// <paramref name="level"/>, that answer in place of <c>auto</c> - a level (thinking on with <c>enabled: auto</c>),
    /// <see cref="Off"/> (thinking off), <see cref="On"/> (thinking on, at the effort written or standing) -, the other
    /// fields as written; without one (a pick under the floor, no answer, the circuit off), the thinking the agent
    /// would have had without <c>auto</c> - <paramref name="standing"/>'s enabled switch and effort, this config's own
    /// written switch, effort and budget first. Null when nothing is left to send: the provider's own thinking applies.
    /// A config with nothing left to System One is returned as is.
    /// </summary>
    /// <param name="level">The answer picked, one of <see cref="Levels"/>, <see cref="Off"/> or <see cref="On"/>; null when none was.</param>
    /// <param name="standing">The thinking that stands without <c>auto</c>: the next config down the chain (the agent's under a task's, else the profile's); null for the provider's own.</param>
    /// <returns>A config with no <c>auto</c> left, or null.</returns>
    public LlmThinkingConfig? Settle(string? level, LlmThinkingConfig? standing)
    {
        if (!HasAuto)
            return this;

        var standingEffort = standing is { AutoEffort: null } ? standing.Effort : null;
        switch (level)
        {
            case Off:
                return new LlmThinkingConfig { Enabled = false };
            case On:
                return this with { AutoEnabled = false, AutoEffort = null, Enabled = true, Effort = AutoEffort is null ? Effort ?? standingEffort : standingEffort };
            case not null:
                return this with { AutoEnabled = false, AutoEffort = null, Enabled = AutoEnabled ? true : Enabled, Effort = ValidateEffort(level) };
        }

        var settled = new LlmThinkingConfig
        {
            Enabled = AutoEnabled ? standing?.Enabled : Enabled ?? standing?.Enabled,
            Effort = AutoEffort is null && Effort is not null ? Effort : standingEffort,
            BudgetTokens = BudgetTokens ?? standing?.BudgetTokens,
        };
        return settled is { Enabled: null, Effort: null, BudgetTokens: null } ? null : settled;
    }

    /// <summary>
    /// The answer among <paramref name="options"/> a settled thinking stands at (#439): <see cref="Off"/> when it is
    /// switched off, <see cref="On"/> when that is offered and it is not off, else its effort; null when the level is
    /// the provider's own, which no answer names.
    /// </summary>
    /// <param name="settled">The thinking a task runs on, settled.</param>
    /// <param name="options">The answers offered (<see cref="AutoOptions"/>).</param>
    /// <returns>The answer, or a level outside <paramref name="options"/>, or null.</returns>
    public static string? AnswerOf(LlmThinkingConfig? settled, IReadOnlyList<string> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (settled is { Enabled: false })
            return Off;
        if (options.Contains(On))
            return On;
        return settled?.Effort is { } effort && RankOf(effort) >= 0 ? effort : null;
    }

    /// <summary>
    /// The answer one step above <paramref name="current"/> among <paramref name="options"/>, cheapest first (#439):
    /// the next one when <paramref name="current"/> is offered, else the first offered above it. Null at the top, and
    /// when <paramref name="current"/> is unknown (null): the raise of a failed attempt then keeps the level.
    /// </summary>
    /// <param name="current">The answer the task stands at (<see cref="AnswerOf"/>).</param>
    /// <param name="options">The answers offered, cheapest first.</param>
    /// <returns>The answer above, or null.</returns>
    public static string? Above(string? current, IReadOnlyList<string> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (current is null)
            return null;
        var index = options.ToList().IndexOf(current);
        if (index >= 0)
            return index + 1 < options.Count ? options[index + 1] : null;
        return options.FirstOrDefault(option => AnswerRank(option) > AnswerRank(current));
    }

    /// <summary>The rank of <paramref name="level"/> in <see cref="Levels"/>, lowest first; -1 when it is no level.</summary>
    internal static int RankOf(string level) => Array.IndexOf(s_levels, level);

    private static int AnswerRank(string answer) => answer switch
    {
        Off => -1,
        On => s_levels.Length,
        _ => RankOf(answer),
    };
}
