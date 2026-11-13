namespace Orkeon.Scripting.Cli.Commands;

/// <summary>What a reasoning step through Orkeon (M15-M17) says of the model capability table (#356, item 10).</summary>
internal enum LlmTableCheck
{
    /// <summary>What Orkeon sent was accepted, or what it lowered or left out the vendor refuses too.</summary>
    Agrees,

    /// <summary>Orkeon sent a value and the vendor refused it: the table is missing a restriction.</summary>
    MissingRestriction,

    /// <summary>Orkeon lowered or left out a value the vendor takes (M18): the table is too narrow.</summary>
    TooNarrow,

    /// <summary>Nothing to judge against: no M18 fragment for the step, or an error that is not a refusal of the value.</summary>
    Unchecked,
}

/// <summary>
/// Confronts each step Orkeon judged (M15-M17) with what the vendor does when the value reaches it unjudged
/// (M18, same run): the campaign checks the table instead of only recording it.
/// <list type="bullet">
/// <item>A step Orkeon sent and the vendor refused (400, 422) is <see cref="LlmTableCheck.MissingRestriction"/>.</item>
/// <item>A step Orkeon lowered or left out is <see cref="LlmTableCheck.TooNarrow"/> when an M18 fragment for the same
/// value was accepted, and for the off switch only when it took effect too (no trace, no reasoning tokens): a vendor that
/// accepts <c>disabled</c> and keeps reasoning does not take it.</item>
/// <item>"On" left out without a warning is <see cref="LlmTableCheck.Agrees"/>: on a model that always reasons, no switch
/// is the switch.</item>
/// </list>
/// </summary>
internal static class ReasoningTableCheck
{
    /// <summary>
    /// Marks every M15-M17 step with its <see cref="LlmTableCheck"/>; a mode holding a contradiction fails, its detail
    /// naming the steps. M18 and the other modes are returned unchanged.
    /// </summary>
    /// <param name="results">The results of one run, M18 among them when it ran.</param>
    public static IReadOnlyList<LlmProbeResult> Apply(IReadOnlyList<LlmProbeResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var raw = results.FirstOrDefault(r => r.Mode == LlmProbeMode.M18)?.Reasoning ?? [];

        return [.. results.Select(result => result.Mode is LlmProbeMode.M15 or LlmProbeMode.M16 or LlmProbeMode.M17
            && result.Reasoning is { Count: > 0 } steps
                ? Checked(result, steps, raw)
                : result)];
    }

    private static LlmProbeResult Checked(LlmProbeResult result, IReadOnlyList<LlmReasoningStep> steps, IReadOnlyList<LlmReasoningStep> raw)
    {
        var checkedSteps = steps.Select(step =>
        {
            var (check, note) = Judge(result.Mode, step, raw);
            return step with { Check = check, CheckNote = note };
        }).ToList();

        var contradictions = checkedSteps
            .Where(s => s.Check is LlmTableCheck.MissingRestriction or LlmTableCheck.TooNarrow)
            .Select(s => $"{s.Step}: {CheckName(s.Check)}")
            .ToList();
        if (contradictions.Count == 0)
            return result with { Reasoning = checkedSteps };

        return result with
        {
            Outcome = LlmProbeOutcome.Failed,
            Detail = $"{result.Detail} · table: {string.Join(", ", contradictions)}",
            Reasoning = checkedSteps,
        };
    }

    /// <summary>The words a report gives a check, also used in the detail of a failed mode.</summary>
    public static string CheckName(LlmTableCheck? check) => check switch
    {
        LlmTableCheck.Agrees => "agrees",
        LlmTableCheck.MissingRestriction => "missing restriction",
        LlmTableCheck.TooNarrow => "too narrow",
        LlmTableCheck.Unchecked => "unchecked",
        _ => "",
    };

    private static (LlmTableCheck, string?) Judge(LlmProbeMode mode, LlmReasoningStep step, IReadOnlyList<LlmReasoningStep> raw)
    {
        switch (step.Outcome)
        {
            case LlmReasoningOutcome.Rejected:
                return step.Wire is not null && IsRefusal(step.Error)
                    ? (LlmTableCheck.MissingRestriction, $"the vendor refused {step.Wire}")
                    : (LlmTableCheck.Unchecked, "the error is not a refusal of the reasoning fields");

            case LlmReasoningOutcome.Lowered or LlmReasoningOutcome.Omitted:
                if (mode == LlmProbeMode.M15 && IsStep(step, "on") && step.Outcome == LlmReasoningOutcome.Omitted && step.Warnings.Count == 0)
                    return (LlmTableCheck.Agrees, "no switch sent: the model always reasons");
                return JudgeNarrowed(mode, step, raw);

            default:
                return (LlmTableCheck.Agrees, null);
        }
    }

    private static (LlmTableCheck, string?) JudgeNarrowed(LlmProbeMode mode, LlmReasoningStep step, IReadOnlyList<LlmReasoningStep> raw)
    {
        var isOff = mode == LlmProbeMode.M15 && IsStep(step, "off");
        var fragments = raw.Where(r => Counterpart(mode, step, r.Step)).ToList();
        if (fragments.Count == 0)
            return (LlmTableCheck.Unchecked, "no M18 fragment for this value");

        var taken = fragments
            .Where(r => r.Outcome == LlmReasoningOutcome.Accepted && (!isOff || StoppedReasoning(r)))
            .Select(r => r.Step)
            .ToList();
        if (taken.Count > 0)
            return (LlmTableCheck.TooNarrow, $"the vendor takes {string.Join(", ", taken)}");

        return fragments.Any(r => r.Outcome == LlmReasoningOutcome.Accepted)
            ? (LlmTableCheck.Agrees, "the vendor accepts the off switch but keeps reasoning")
            : (LlmTableCheck.Agrees, $"the vendor refuses {string.Join(", ", fragments.Select(r => r.Step))}");
    }

    // The M18 labels of the catalog: `effort:<value>`, `toggle:on|off`, `off:<value>@<effort>` (Anthropic), `budget:<n>`.
    private static bool Counterpart(LlmProbeMode mode, LlmReasoningStep step, string label) => mode switch
    {
        LlmProbeMode.M15 when IsStep(step, "on") => Same(label, "toggle:on"),
        LlmProbeMode.M15 when IsStep(step, "off") => Same(label, "toggle:off") || label.StartsWith("off:", StringComparison.OrdinalIgnoreCase),
        LlmProbeMode.M16 => Same(label, $"effort:{step.Step}"),
        LlmProbeMode.M17 => label.StartsWith("budget:", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    private static bool StoppedReasoning(LlmReasoningStep step) =>
        !step.Trace && step.Vendor?.Trace != true && (step.ReasoningTokens ?? step.Vendor?.ReasoningTokens ?? 0) == 0;

    // A refusal of the request's content, not of the account or the service: 429, 5xx and timeouts judge nothing.
    private static bool IsRefusal(string? error) =>
        error is not null
        && (error.StartsWith("400", StringComparison.Ordinal)
            || error.StartsWith("422", StringComparison.Ordinal)
            || error.Contains("BadRequest", StringComparison.Ordinal)
            || error.Contains("UnprocessableEntity", StringComparison.Ordinal));

    private static bool IsStep(LlmReasoningStep step, string name) => Same(step.Step, name);

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
