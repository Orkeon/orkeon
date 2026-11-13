using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Application.Common.DTOs;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Scripting.Cli.Commands;

/// <summary>M15-M17: the reasoning controls of <see cref="LlmThinkingConfig"/> and what really went on the wire.</summary>
internal sealed partial class LlmProbeRunner
{
    // A puzzle a model knows by heart is answered without thinking under adaptive reasoning
    // (Claude spent 5 completion tokens on the bat-and-ball one, no trace at any effort): this
    // one needs a few counting steps, so a trace can come back. The answer is 208.
    private const string ReasoningPrompt =
        "How many positive integers below 1000 are divisible by 7 or by 11, but not by both? "
        + "Answer with the number only.";

    private const int ReasoningMaxTokens = 2048;
    private const int ReasoningBudgetTokens = 1024;
    private const int ErrorExcerpt = 200;

    private enum StepKind
    {
        Toggle,
        Effort,
        Budget,
    }

    private sealed record ReasoningStepSpec(string Name, StepKind Kind, LlmThinkingConfig Thinking, string Asked);

    private async Task<(LlmProbeOutcome, string, IReadOnlyList<LlmReasoningStep>?)> ProbeReasoningAsync(
        LlmProbeMode mode, LlmConfig config, CancellationToken cancellationToken)
    {
        var support = _provider.Capabilities.Thinking;
        if (support == ThinkingSupport.None)
            return (LlmProbeOutcome.NotApplicable, "the provider declares no thinking capability", null);

        if (mode == LlmProbeMode.M15 && support < ThinkingSupport.Toggle)
            return (LlmProbeOutcome.NotApplicable, "the provider declares no thinking toggle", null);

        var specs = SpecsFor(mode);
        var steps = new List<LlmReasoningStep>();
        foreach (var spec in specs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            steps.Add(await RunReasoningStepAsync(spec, config, cancellationToken).ConfigureAwait(false));
        }

        var outcome = steps.Any(s => s.Outcome == LlmReasoningOutcome.Rejected)
            ? LlmProbeOutcome.Failed
            : steps.Any(s => s.Outcome is LlmReasoningOutcome.Sent or LlmReasoningOutcome.Lowered)
                ? LlmProbeOutcome.Passed
                : LlmProbeOutcome.NotApplicable;

        return (outcome, FormatSteps(steps), steps);
    }

    private List<ReasoningStepSpec> SpecsFor(LlmProbeMode mode)
    {
        switch (mode)
        {
            case LlmProbeMode.M15:
                return
                [
                    new("on", StepKind.Toggle, new LlmThinkingConfig { Enabled = true }, "true"),
                    new("off", StepKind.Toggle, new LlmThinkingConfig { Enabled = false }, "false"),
                ];
            case LlmProbeMode.M16:
                var levels = SweepEfforts is { Count: > 0 } ? SweepEfforts : DefaultSweep();
                return [.. levels.Select(l => new ReasoningStepSpec(l, StepKind.Effort, new LlmThinkingConfig { Effort = l }, l))];
            default:
                return
                [
                    new("budget", StepKind.Budget,
                        new LlmThinkingConfig { BudgetTokens = ReasoningBudgetTokens },
                        ReasoningBudgetTokens.ToString(CultureInfo.InvariantCulture)),
                ];
        }
    }

    /// <summary>
    /// The five levels, then every vendor value M18's <c>effort:*</c> fragments name (<c>none</c>, <c>minimal</c>): a value
    /// M18 measures is also sent through Orkeon, so the table check can judge it (#356, item 10).
    /// </summary>
    private List<string> DefaultSweep()
    {
        var sweep = new List<string>(LlmThinkingConfig.Levels);
        foreach (var (label, _) in RawReasoning)
        {
            if (!label.StartsWith("effort:", StringComparison.OrdinalIgnoreCase))
                continue;
            var value = label["effort:".Length..].Trim();
            if (value.Length > 0 && !sweep.Contains(value, StringComparer.OrdinalIgnoreCase))
                sweep.Add(value);
        }

        return sweep;
    }

    private async Task<LlmReasoningStep> RunReasoningStepAsync(
        ReasoningStepSpec spec, LlmConfig config, CancellationToken cancellationToken)
    {
        WireProbe?.Reset();
        var stepConfig = config with { Thinking = spec.Thinking, MaxTokens = ReasoningMaxTokens };

        var (response, error) = await SendReasoningAsync(stepConfig, cancellationToken).ConfigureAwait(false);

        var wire = WireProbe?.LastFields;
        var warnings = WireProbe?.Warnings ?? [];

        var (outcome, sentValue) = error is not null
            ? (LlmReasoningOutcome.Rejected, null)
            : Classify(spec, wire);

        var metadata = response?.Metadata;
        return new LlmReasoningStep(
            spec.Name,
            outcome,
            sentValue,
            wire,
            metadata?.ContainsKey("reasoning_content") ?? false,
            ReadCount(metadata, LlmUsageMetadataKeys.ReasoningTokens),
            response?.CompletionTokens,
            metadata is not null && metadata.TryGetValue("finish_reason", out var finish) ? finish?.ToString() : null,
            warnings,
            error is null ? null : Truncate(error, ErrorExcerpt),
            WireProbe?.LastAnswer);
    }

    private async Task<(LlmResponse? Response, string? Error)> SendReasoningAsync(
        LlmConfig stepConfig, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _provider.GenerateAsync(ReasoningPrompt, stepConfig, cancellationToken)
                .ConfigureAwait(false);
            return (response, ErrorOfStep(response));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A thrown vendor refusal is the finding of the step, not a crash of the campaign.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            var status = ex is HttpRequestException { StatusCode: { } code } ? $"{(int)code} " : "";
            return (null, $"{status}{ex.Message}");
        }
    }

    /// <summary>
    /// M18 - the raw reasoning matrix. The provider builds the request (auth, URL, headers,
    /// dialect) but the wire recorder rewrites its reasoning fields to each fragment just before
    /// the request leaves, so what is measured is the vendor, not Orkeon's capability judgement.
    /// </summary>
    private async Task<(LlmProbeOutcome, string, IReadOnlyList<LlmReasoningStep>?)> ProbeRawReasoningAsync(
        LlmConfig config, CancellationToken cancellationToken)
    {
        if (RawReasoning.Count == 0)
            return (LlmProbeOutcome.NotApplicable, "no raw reasoning fragments", null);

        var steps = new List<LlmReasoningStep>();
        foreach (var (label, body) in RawReasoning)
        {
            cancellationToken.ThrowIfCancellationRequested();
            steps.Add(await RunRawReasoningStepAsync(label, body, config, cancellationToken).ConfigureAwait(false));
        }

        var accepted = steps.Count(s => s.Outcome == LlmReasoningOutcome.Accepted);
        var refused = steps.Where(s => s.Outcome == LlmReasoningOutcome.Refused).Select(s => s.Step).ToList();
        var detail = $"{accepted} accepted · {refused.Count} refused"
            + (refused.Count == 0 ? "" : $" ({Truncate(string.Join(", ", refused), 120)})");
        return (accepted > 0 ? LlmProbeOutcome.Passed : LlmProbeOutcome.Failed, detail, steps);
    }

    private async Task<LlmReasoningStep> RunRawReasoningStepAsync(
        string label, JsonObject body, LlmConfig config, CancellationToken cancellationToken)
    {
        WireProbe?.Reset();
        // Orkeon's own reasoning intent is dropped: the fragment is the only reasoning on the wire.
        var stepConfig = config with { Thinking = null, MaxTokens = ReasoningMaxTokens };

        LlmResponse? response;
        string? error;
        WireProbe?.Override(body);
        try
        {
            (response, error) = await SendReasoningAsync(stepConfig, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            WireProbe?.Override(null);
        }

        var metadata = response?.Metadata;
        return new LlmReasoningStep(
            label,
            error is null ? LlmReasoningOutcome.Accepted : LlmReasoningOutcome.Refused,
            null,
            WireProbe?.LastFields,
            metadata?.ContainsKey("reasoning_content") ?? false,
            ReadCount(metadata, LlmUsageMetadataKeys.ReasoningTokens),
            response?.CompletionTokens,
            metadata is not null && metadata.TryGetValue("finish_reason", out var finish) ? finish?.ToString() : null,
            WireProbe?.Warnings ?? [],
            error is null ? null : Truncate(error, ErrorExcerpt),
            WireProbe?.LastAnswer);
    }

    private static string? ErrorOfStep(LlmResponse response)
    {
        if (HasError(response))
            return ErrorOf(response);

        if (response.Metadata.TryGetValue("http_status_code", out var status)
            && int.TryParse(Convert.ToString(status, CultureInfo.InvariantCulture), out var code)
            && code >= 400)
        {
            var body = response.Metadata.TryGetValue("error_content", out var content) ? content?.ToString() : null;
            return $"{code} {body}".Trim();
        }

        return null;
    }

    private static long? ReadCount(IReadOnlyDictionary<string, object>? metadata, string key)
    {
        if (metadata is null || !metadata.TryGetValue(key, out var value) || value is null)
            return null;

        if (value is JsonElement { ValueKind: JsonValueKind.Number } element)
            return element.TryGetInt64(out var n) ? n : null;

        return long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private (LlmReasoningOutcome, string?) Classify(ReasoningStepSpec spec, string? wire)
    {
        // Without a capture the response alone is all there is: no error counts as sent.
        if (WireProbe is null)
            return (LlmReasoningOutcome.Sent, null);

        if (wire is null)
            return (LlmReasoningOutcome.Omitted, null);

        using var doc = JsonDocument.Parse(wire);
        var root = doc.RootElement;
        var found = spec.Kind switch
        {
            StepKind.Effort => FirstOf(root, [["reasoning_effort"], ["output_config", "effort"], ["reasoning", "effort"]], stringOnly: false)
                ?? StringThink(root),
            StepKind.Toggle => FirstOf(root, [["thinking", "type"], ["enable_thinking"], ["chat_template_kwargs", "enable_thinking"], ["reasoning", "enabled"]], stringOnly: false)
                ?? BoolThink(root),
            _ => FirstOf(root, [["thinking_budget"], ["reasoning", "max_tokens"], ["thinking", "budget_tokens"]], stringOnly: false),
        };

        if (found is null)
            return (LlmReasoningOutcome.Omitted, null);

        if (spec.Kind == StepKind.Effort
            && !string.Equals(found, spec.Asked, StringComparison.OrdinalIgnoreCase))
            return (LlmReasoningOutcome.Lowered, found);

        return (LlmReasoningOutcome.Sent, found);
    }

    private static string? StringThink(JsonElement root) =>
        root.TryGetProperty("think", out var think) && think.ValueKind == JsonValueKind.String ? think.GetString() : null;

    private static string? BoolThink(JsonElement root) =>
        root.TryGetProperty("think", out var think) && think.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? think.GetRawText()
            : null;

    private static string? FirstOf(JsonElement root, string[][] paths, bool stringOnly)
    {
        foreach (var path in paths)
        {
            var current = root;
            var ok = true;
            foreach (var segment in path)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                {
                    ok = false;
                    break;
                }
            }

            if (!ok || current.ValueKind == JsonValueKind.Null)
                continue;
            if (stringOnly && current.ValueKind != JsonValueKind.String)
                continue;

            return current.ValueKind == JsonValueKind.String ? current.GetString() : current.GetRawText();
        }

        return null;
    }

    private static string FormatSteps(IReadOnlyList<LlmReasoningStep> steps) =>
        string.Join(" · ", steps.Select(FormatStep));

    private static string FormatStep(LlmReasoningStep step)
    {
        var name = step.Outcome == LlmReasoningOutcome.Lowered ? $"{step.Step}→{step.SentValue}" : step.Step;
        var text = step.Outcome switch
        {
            LlmReasoningOutcome.Rejected => $"{name} ❌ {step.Error}",
            LlmReasoningOutcome.Omitted => $"{name} ➖ omitted",
            LlmReasoningOutcome.Refused => $"{name} ❌ {step.Error}",
            _ => $"{name} ✅",
        };

        return step.Warnings.Count == 0
            ? text
            : $"{text} ({string.Join("; ", step.Warnings.Select(w => Truncate(w, 60)))})";
    }
}
