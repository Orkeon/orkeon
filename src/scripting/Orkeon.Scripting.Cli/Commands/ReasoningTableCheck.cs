using System.Text.RegularExpressions;

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

    /// <summary>Nothing to judge against: no M18 fragment for the step, an error that is not a refusal of the value, or a
    /// vendor whose acceptance proves nothing.</summary>
    Unchecked,
}

/// <summary>
/// Confronts each step Orkeon judged (M15-M17) with what the vendor does when the value reaches it unjudged
/// (M18, same run): the campaign checks the table instead of only recording it.
/// <list type="bullet">
/// <item>A step Orkeon sent and the vendor refused (HTTP 400 or 422) is <see cref="LlmTableCheck.MissingRestriction"/>,
/// unless M18 had the same fields accepted: the refusal is then about something else.</item>
/// <item>A step Orkeon lowered or left out is <see cref="LlmTableCheck.TooNarrow"/> when an M18 fragment for the same
/// value was accepted. A value that switches reasoning off (the off switch, the effort <c>none</c>) must also have
/// stopped it; and when the vendor accepts <c>effort:none</c> yet keeps reasoning, it swallows the effort field, so an
/// accepted effort proves nothing and the step is <see cref="LlmTableCheck.Unchecked"/>.</item>
/// <item>"On" left out without a thinking warning is <see cref="LlmTableCheck.Agrees"/>: on a model that always reasons,
/// no switch is the switch.</item>
/// </list>
/// </summary>
internal static partial class ReasoningTableCheck
{
    // Below this many reasoning tokens, an answer did not reason (kimi-k2.6 reports 1 with thinking off).
    private const long StoppedTokens = 5;

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

    /// <summary>The words a report gives a check, also used in the detail of a failed mode.</summary>
    public static string CheckName(LlmTableCheck? check) => check switch
    {
        LlmTableCheck.Agrees => "agrees",
        LlmTableCheck.MissingRestriction => "missing restriction",
        LlmTableCheck.TooNarrow => "too narrow",
        LlmTableCheck.Unchecked => "unchecked",
        _ => "",
    };

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

    private static (LlmTableCheck, string?) Judge(LlmProbeMode mode, LlmReasoningStep step, IReadOnlyList<LlmReasoningStep> raw)
    {
        switch (step.Outcome)
        {
            case LlmReasoningOutcome.Rejected:
                return JudgeRefusal(mode, step, raw);

            case LlmReasoningOutcome.Lowered or LlmReasoningOutcome.Omitted:
                if (mode == LlmProbeMode.M15 && IsStep(step, "on") && step.Outcome == LlmReasoningOutcome.Omitted
                    && !step.Warnings.Any(w => w.Contains("'thinking", StringComparison.OrdinalIgnoreCase)))
                    return (LlmTableCheck.Agrees, "no switch sent: the model always reasons");
                return JudgeNarrowed(mode, step, raw);

            default:
                return (LlmTableCheck.Agrees, null);
        }
    }

    private static (LlmTableCheck, string?) JudgeRefusal(LlmProbeMode mode, LlmReasoningStep step, IReadOnlyList<LlmReasoningStep> raw)
    {
        if (step.Wire is null || !IsRefusal(step.Error))
            return (LlmTableCheck.Unchecked, "the error is not a refusal of the reasoning fields");

        var same = raw.FirstOrDefault(r => Counterpart(mode, step, r.Step)
            && r.Outcome == LlmReasoningOutcome.Accepted
            && SameJson(r.Wire, step.Wire));
        return same is not null
            ? (LlmTableCheck.Unchecked, $"M18 had the same fields accepted ({same.Step}): the refusal is about something else")
            : (LlmTableCheck.MissingRestriction, $"the vendor refused {step.Wire}");
    }

    private static (LlmTableCheck, string?) JudgeNarrowed(LlmProbeMode mode, LlmReasoningStep step, IReadOnlyList<LlmReasoningStep> raw)
    {
        var fragments = raw.Where(r => Counterpart(mode, step, r.Step)).ToList();
        if (fragments.Count == 0)
            return (LlmTableCheck.Unchecked, "no M18 fragment for this value");

        var accepted = fragments.Where(r => r.Outcome == LlmReasoningOutcome.Accepted).ToList();
        if (accepted.Count == 0)
            return (LlmTableCheck.Agrees, $"the vendor refuses {string.Join(", ", fragments.Select(r => r.Step))}");

        var switchesOff = (mode == LlmProbeMode.M15 && IsStep(step, "off")) || (mode == LlmProbeMode.M16 && IsStep(step, "none"));
        if (!switchesOff)
        {
            return SwallowsEffort(raw)
                ? (LlmTableCheck.Unchecked, "the vendor accepts effort:none and keeps reasoning: an accepted effort proves nothing")
                : (LlmTableCheck.TooNarrow, $"the vendor takes {string.Join(", ", accepted.Select(r => r.Step))}");
        }

        var stopped = accepted.Select(r => (r.Step, Stopped: Stopped(r, raw))).ToList();
        var taken = stopped.Where(s => s.Stopped == true).Select(s => s.Step).ToList();
        if (taken.Count > 0)
            return (LlmTableCheck.TooNarrow, $"the vendor takes {string.Join(", ", taken)}");

        return stopped.Any(s => s.Stopped is null)
            ? (LlmTableCheck.Unchecked, "accepted, but the answer cannot tell whether reasoning stopped")
            : (LlmTableCheck.Agrees, "the vendor accepts it but keeps reasoning");
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

    private static bool SwallowsEffort(IReadOnlyList<LlmReasoningStep> raw)
    {
        var none = raw.FirstOrDefault(r => Same(r.Step, "effort:none") && r.Outcome == LlmReasoningOutcome.Accepted);
        return none is not null && Stopped(none, raw) == false;
    }

    /// <summary>
    /// Whether an accepted fragment stopped the reasoning: no trace and at most a handful of reasoning tokens when the run
    /// shows reasoning at all; otherwise (Anthropic without <c>display</c> reports neither) half the completion tokens of the
    /// fragment that spent most, or null when nothing tells.
    /// </summary>
    private static bool? Stopped(LlmReasoningStep fragment, IReadOnlyList<LlmReasoningStep> raw)
    {
        if (Reasoned(fragment))
            return false;

        if (raw.Any(r => r.Outcome == LlmReasoningOutcome.Accepted && Reasoned(r)))
            return true;

        var reference = raw.Where(r => r.Outcome == LlmReasoningOutcome.Accepted && r.CompletionTokens is not null)
            .Select(r => r.CompletionTokens!.Value)
            .DefaultIfEmpty(0)
            .Max();
        if (fragment.CompletionTokens is not { } spent || reference == 0)
            return null;

        return spent * 2 <= reference;
    }

    private static bool Reasoned(LlmReasoningStep step) =>
        step.Trace || step.Vendor?.Trace == true || (step.ReasoningTokens ?? step.Vendor?.ReasoningTokens ?? 0) > StoppedTokens;

    // A refusal of the request's content, read on the status: `<Provider> API error: BadRequest - ...` or `400 ...`.
    // A 429, a 5xx (whose body may say "BadRequestError") or a timeout judges nothing.
    private static bool IsRefusal(string? error)
    {
        if (error is null)
            return false;

        var match = StatusPattern().Match(error);
        return match.Success && match.Groups["status"].Value is "BadRequest" or "UnprocessableEntity" or "400" or "422";
    }

    [GeneratedRegex(@"^(?:(?<status>\d{3})\b|.*?API error: (?<status>\w+))", RegexOptions.CultureInvariant)]
    private static partial Regex StatusPattern();

    private static bool SameJson(string? a, string? b)
    {
        if (a is null || b is null)
            return false;
        try
        {
            return System.Text.Json.Nodes.JsonNode.DeepEquals(
                System.Text.Json.Nodes.JsonNode.Parse(a), System.Text.Json.Nodes.JsonNode.Parse(b));
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static bool IsStep(LlmReasoningStep step, string name) => Same(step.Step, name);

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
