using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.Capabilities;

namespace Orkeon.Infrastructure.LLMs.Base;

/// <summary>The reasoning controls every dialect judges the same way, against the provider and the model (#356).</summary>
public abstract partial class HttpLlmProviderBase
{
    private readonly ConcurrentDictionary<string, byte> _uncheckedEffortModels = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The per-model table this provider reads for audio input and reasoning (FWK-21, #356): the built-in one
    /// until <see cref="LlmProviderFactory"/> hands it the host's, overrides included.
    /// </summary>
    internal IModelCapabilityRegistry ModelCapabilities { get; set; } = ModelCapabilityRegistry.Default;

    /// <summary>
    /// Judges <paramref name="thinking"/> for <paramref name="model"/> with <see cref="ReasoningJudgement"/> and logs
    /// what it dropped or changed: an unsupported option, a lowered level, and, once per provider instance and
    /// model, an effort the table could not check.
    /// </summary>
    /// <param name="thinking">The caller's options, or null.</param>
    /// <param name="model">The model the call is sent to.</param>
    /// <param name="providerName">The provider's name in the warnings.</param>
    /// <param name="budgetRemedy">The remedy for a budget the provider cannot carry, or null for the default one.</param>
    private protected ReasoningVerdict JudgeReasoning(
        LlmThinkingConfig? thinking, string model, string providerName, string? budgetRemedy = null)
    {
        var verdict = budgetRemedy is null
            ? ReasoningJudgement.Judge(thinking, Capabilities, ModelCapabilities.TryGet(model, Name), model)
            : ReasoningJudgement.Judge(thinking, Capabilities, ModelCapabilities.TryGet(model, Name), model, budgetRemedy);

        foreach (var notice in verdict.Notices)
        {
            switch (notice.Kind)
            {
                case ReasoningNoticeKind.Unsupported:
                    LogReasoningOptionDropped(notice.Option, providerName, notice.Remedy);
                    break;
                case ReasoningNoticeKind.EffortLowered:
                    LogReasoningEffortLowered(notice.Asked, providerName, model, notice.Known, notice.Sent);
                    break;
                case ReasoningNoticeKind.EffortUnchecked:
                    // Once per provider instance and model: the same notice on every call would drown the log.
                    if (_uncheckedEffortModels.TryAdd(model, 0))
                        LogReasoningEffortUnchecked(model, providerName, notice.Asked);
                    break;
            }
        }

        return verdict;
    }

    /// <summary>An option the caller declared that the provider or the model cannot honour is reported, not dropped (LLM-02).</summary>
    [LoggerMessage(EventId = 110, Level = LogLevel.Warning,
        Message = "Option '{Option}' was declared but {ProviderName} does not support it — it was not sent. {Remedy}.")]
    private partial void LogReasoningOptionDropped(string option, string providerName, string remedy);

    /// <summary>A level the model does not take: the nearest one it takes is sent, and the caller is told.</summary>
    [LoggerMessage(EventId = 112, Level = LogLevel.Warning,
        Message = "Effort '{Effort}' was declared but {ProviderName} takes {Known} only for model '{Model}' — '{Sent}' was sent in its place. Ask for a level it takes, or pick a model that takes this one.")]
    private partial void LogReasoningEffortLowered(string effort, string providerName, string model, string known, string sent);

    /// <summary>The model is in no table, so its effort could not be checked: it is sent as asked.</summary>
    [LoggerMessage(EventId = 113, Level = LogLevel.Warning,
        Message = "The model '{Model}' of {ProviderName} is in no capability table, so its reasoning effort '{Effort}' was sent unchecked. Declare it with Llm:ModelCapabilities:<id>:ReasoningEfforts to silence this.")]
    private partial void LogReasoningEffortUnchecked(string model, string providerName, string effort);
}
