using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.LLMs.Capabilities;

/// <summary>
/// What a call may send for reasoning, once judged against the provider and the model (#356). Each dialect
/// writes these values in its own fields; none judges the caller's <see cref="LlmThinkingConfig"/> again.
/// </summary>
/// <param name="Support">The effective support: the lower of the provider's and the model's.</param>
/// <param name="Toggle">The on/off switch to send, or null to send none.</param>
/// <param name="AskedOn">Whether the caller asked for thinking on and the model reasons (sent or not as a toggle).</param>
/// <param name="Effort">The effort to send (lowered when needed), or null to send none.</param>
/// <param name="BudgetTokens">The reasoning budget to send, or null to send none.</param>
/// <param name="Notices">What was dropped or changed, for the provider to log: never a silent drop (LLM-02).</param>
internal sealed record ReasoningVerdict(
    ThinkingSupport Support,
    bool? Toggle,
    bool AskedOn,
    string? Effort,
    int? BudgetTokens,
    IReadOnlyList<ReasoningNotice> Notices)
{
    /// <summary>Nothing to send and nothing to say: the caller set no thinking option.</summary>
    public static ReasoningVerdict Empty { get; } = new(ThinkingSupport.None, null, false, null, null, []);
}

/// <summary>The kind of a <see cref="ReasoningNotice"/>, so a provider routes it to its own log message.</summary>
internal enum ReasoningNoticeKind
{
    /// <summary>An option the provider or the model cannot honour was not sent.</summary>
    Unsupported,

    /// <summary>An effort level was lowered (or raised, when nothing lies below) to one the model takes.</summary>
    EffortLowered,

    /// <summary>The model is in no table: the effort is sent as asked, unchecked.</summary>
    EffortUnchecked,
}

/// <summary>One thing the judgement changed or dropped.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="Option">The option concerned (<c>thinking</c>, <c>thinking.enabled</c>, <c>thinking.effort</c>, <c>thinking.budgetTokens</c>).</param>
/// <param name="Remedy">For <see cref="ReasoningNoticeKind.Unsupported"/>, what the caller can do.</param>
/// <param name="Asked">For an effort notice, the level asked.</param>
/// <param name="Known">For a lowered effort, the levels the model takes.</param>
/// <param name="Sent">For an effort notice, the level sent.</param>
internal sealed record ReasoningNotice(
    ReasoningNoticeKind Kind,
    string Option,
    string Remedy = "",
    string Asked = "",
    string Known = "",
    string Sent = "");

/// <summary>
/// The one rule every provider applies to <see cref="LlmThinkingConfig"/> (#356): the provider's declared
/// <see cref="LlmProviderCapabilities"/> narrowed by the model's entry of the table.
/// <list type="bullet">
/// <item>Thinking support is the lower of the two; <see cref="ThinkingSupport.None"/> drops every option.</item>
/// <item>The off switch needs <see cref="ThinkingSupport.Toggle"/>: on a model that always reasons it is dropped
/// with a warning, while asking for thinking on is already honoured and sends nothing.</item>
/// <item>A level is lowered with <see cref="LlmThinkingConfig.EffortFor"/> to the provider's levels narrowed by the
/// model's <see cref="ModelCapabilities.ReasoningEfforts"/>; with none left, the effort is dropped. A vendor value
/// outside <see cref="LlmThinkingConfig.Levels"/> (<c>none</c>, <c>minimal</c>) is sent when the model's list has it
/// or the model is unknown, and dropped otherwise.</item>
/// <item>A budget needs a provider declaring <see cref="ThinkingSupport.Budget"/>; a model that cannot switch thinking off
/// (<see cref="ThinkingSupport.EffortOnly"/>) keeps it, since the gateway that carries it converts it (OpenRouter on
/// Gemini 3.7, campaign M18).</item>
/// </list>
/// </summary>
internal static class ReasoningJudgement
{
    /// <summary>Judges <paramref name="thinking"/> for <paramref name="model"/> behind a provider declaring <paramref name="provider"/>.</summary>
    /// <param name="thinking">The caller's options, or null.</param>
    /// <param name="provider">The provider's declared capabilities.</param>
    /// <param name="entry">The model's entry of the table, or null when the table does not know it.</param>
    /// <param name="model">The model id, for the remedies.</param>
    /// <param name="budgetRemedy">The remedy a provider below <see cref="ThinkingSupport.Budget"/> gives for a budget.</param>
    public static ReasoningVerdict Judge(
        LlmThinkingConfig? thinking,
        LlmProviderCapabilities provider,
        ModelCapabilities? entry,
        string model,
        string budgetRemedy = "this API takes no reasoning token budget; use the effort hint instead")
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (thinking is null)
            return ReasoningVerdict.Empty;

        var notices = new List<ReasoningNotice>();
        var support = Effective(provider.Thinking, entry?.Thinking);
        if (support == ThinkingSupport.None)
        {
            notices.Add(new(ReasoningNoticeKind.Unsupported, "thinking",
                entry?.Thinking == ThinkingSupport.None && provider.Thinking != ThinkingSupport.None
                    ? $"the model '{model}' has no reasoning pass; remove the option or pick a reasoning model"
                    : "this provider offers no thinking control - no reasoning pass, or one that is always on; remove the option or pick a provider that does"));
            return new ReasoningVerdict(support, null, false, null, null, notices);
        }

        var (toggle, askedOn) = JudgeToggle(thinking, support, provider, entry, model, notices);
        var effort = JudgeEffort(thinking, provider, entry, model, notices);

        int? budget = null;
        if (thinking.BudgetTokens.HasValue)
        {
            if (provider.Thinking >= ThinkingSupport.Budget)
                budget = thinking.BudgetTokens;
            else
                notices.Add(new(ReasoningNoticeKind.Unsupported, "thinking.budgetTokens", budgetRemedy));
        }

        return new ReasoningVerdict(support, toggle, askedOn, effort, budget, notices);
    }

    private static ThinkingSupport Effective(ThinkingSupport provider, ThinkingSupport? model)
        => model is { } narrowed && narrowed < provider ? narrowed : provider;

    private static (bool? Toggle, bool AskedOn) JudgeToggle(
        LlmThinkingConfig thinking, ThinkingSupport support, LlmProviderCapabilities provider,
        ModelCapabilities? entry, string model, List<ReasoningNotice> notices)
    {
        if (thinking.Enabled is not { } enabled)
            return (null, false);

        if (support >= ThinkingSupport.Toggle)
            return (enabled, enabled);

        // The model reasons and cannot stop: "on" is what it does anyway, "off" cannot be honoured.
        if (enabled)
            return (null, true);

        notices.Add(new(ReasoningNoticeKind.Unsupported, "thinking.enabled",
            entry?.Thinking is { } narrowed && narrowed < provider.Thinking
                ? $"the model '{model}' always reasons and cannot switch thinking off; remove the option, or pick a model that can"
                : "this API always decides on its own whether to reason; only the effort hint is honoured"));
        return (null, false);
    }

    private static string? JudgeEffort(
        LlmThinkingConfig thinking, LlmProviderCapabilities provider, ModelCapabilities? entry, string model,
        List<ReasoningNotice> notices)
    {
        if (string.IsNullOrWhiteSpace(thinking.Effort))
            return null;

        // Names compare ignoring case, and the wire gets the canonical spelling (`HIGH` is sent `high`).
        var asked = LlmThinkingConfig.Levels.FirstOrDefault(level => string.Equals(level, thinking.Effort.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? thinking.Effort.Trim();
        var modelEfforts = entry?.ReasoningEfforts;

        if (modelEfforts is { Count: 0 })
        {
            notices.Add(new(ReasoningNoticeKind.Unsupported, "thinking.effort",
                $"the model '{model}' takes no reasoning effort; remove the option or pick a reasoning model"));
            return null;
        }

        // A vendor's own value (`none`, `minimal`) is no level: the model's list judges it, else the vendor does.
        if (!LlmThinkingConfig.Levels.Contains(asked))
        {
            var listed = modelEfforts?.FirstOrDefault(value => string.Equals(value, asked, StringComparison.OrdinalIgnoreCase));
            if (modelEfforts is null || listed is not null)
            {
                if (entry is null)
                    notices.Add(new(ReasoningNoticeKind.EffortUnchecked, "thinking.effort", Asked: asked, Sent: asked));
                return listed ?? asked;
            }

            notices.Add(new(ReasoningNoticeKind.Unsupported, "thinking.effort",
                $"the model '{model}' takes the efforts {string.Join(", ", modelEfforts)} only; ask for one of them"));
            return null;
        }

        var known = modelEfforts is null
            ? provider.EffortLevels
            : provider.EffortLevels.Where(level => modelEfforts.Contains(level, StringComparer.OrdinalIgnoreCase)).ToList();
        if (known.Count == 0)
        {
            notices.Add(new(ReasoningNoticeKind.Unsupported, "thinking.effort",
                $"the model '{model}' takes the efforts {string.Join(", ", modelEfforts!)} only, none of which this provider's field carries; remove the option"));
            return null;
        }

        var sent = LlmThinkingConfig.EffortFor(asked, known);
        if (sent != asked)
            notices.Add(new(ReasoningNoticeKind.EffortLowered, "thinking.effort", Asked: asked, Known: string.Join(", ", known), Sent: sent));
        else if (entry is null)
            notices.Add(new(ReasoningNoticeKind.EffortUnchecked, "thinking.effort", Asked: asked, Sent: sent));
        return sent;
    }
}
