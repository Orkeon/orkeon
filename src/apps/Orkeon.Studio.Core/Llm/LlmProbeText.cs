using System.Globalization;
using Orkeon.Studio.Core.Localization;

namespace Orkeon.Studio.Core.Llm;

/// <summary>
/// Describes an <see cref="LlmProbeResult"/> in the interface's language (STUDIO-43): for a
/// failure, the step, the URL called, the time waited and the cause; for a success, what
/// answered. The cause's own detail — an error body, an exception chain — stays as the endpoint
/// or the runtime wrote it: it is evidence, not copy.
/// </summary>
public static class LlmProbeText
{
    /// <summary>Describes <paramref name="result"/> through <paramref name="strings"/>.</summary>
    /// <param name="result">The verdict to describe.</param>
    /// <param name="strings">The localization port of the calling front.</param>
    /// <param name="culture">Formats the numbers; the current culture when null.</param>
    public static string Describe(LlmProbeResult result, IStudioStrings strings, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(strings);
        culture ??= CultureInfo.CurrentCulture;

        if (result.Succeeded)
            return DescribeSuccess(result, strings, culture);

        var cause = Cause(result, strings, culture);

        // A configuration that cannot be probed, or a verdict with no request behind it (a front
        // reporting its own failure through Unreachable), has no step nor URL to name.
        if (result.Stage == LlmProbeStage.Configuration || result.Url is null)
            return strings.Format(culture, StudioStringKeys.ProbeFailed, cause);

        var stage = strings[result.Stage == LlmProbeStage.Completion
            ? StudioStringKeys.ProbeStageCompletion
            : StudioStringKeys.ProbeStageModels];

        return strings.Format(culture, StudioStringKeys.ProbeFailedAtStage,
            stage, result.Url, Seconds(result.Elapsed, culture), cause);
    }

    private static string DescribeSuccess(LlmProbeResult result, IStudioStrings strings, CultureInfo culture)
    {
        if (result.Stage == LlmProbeStage.Completion && result.Model is { } model)
        {
            return strings.Format(culture, StudioStringKeys.ProbeCompletionPassed,
                model, Seconds(result.Elapsed, culture));
        }

        return result.ModelCount is { } count
            ? strings.Format(culture, StudioStringKeys.ProbeReachable, count.ToString(culture))
            : strings[StudioStringKeys.ProbeReachableNoCount];
    }

    private static string Cause(LlmProbeResult result, IStudioStrings strings, CultureInfo culture) =>
        result.Failure switch
        {
            LlmProbeFailure.NoBaseUrl => strings[StudioStringKeys.ProbeNoBaseUrl],
            LlmProbeFailure.NotAbsoluteUrl => strings.Format(culture, StudioStringKeys.ProbeNotAbsoluteUrl, result.Detail ?? ""),
            LlmProbeFailure.NotHttpUrl => strings.Format(culture, StudioStringKeys.ProbeNotHttpUrl, result.Detail ?? ""),
            LlmProbeFailure.NoCatalogue => strings[StudioStringKeys.ProbeNoCatalogue],
            LlmProbeFailure.Timeout => strings.Format(culture, StudioStringKeys.ProbeTimeout,
                result.Timeout.TotalSeconds.ToString("0.#", culture)),
            LlmProbeFailure.HttpStatus => strings.Format(culture, StudioStringKeys.ProbeHttpStatus,
                $"{result.StatusCode} {result.ReasonPhrase}".Trim(), result.Detail ?? "").TrimEnd(),
            _ => result.Detail ?? "",
        };

    private static string Seconds(TimeSpan elapsed, CultureInfo culture) =>
        elapsed.TotalSeconds.ToString("0.0", culture);
}
