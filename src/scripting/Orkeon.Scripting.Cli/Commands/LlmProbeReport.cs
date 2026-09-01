using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orkeon.Scripting.Cli.Commands;

/// <summary>Identifies the run itself, so a report can be trusted months later.</summary>
/// <param name="Provider">Provider key as it appears in the matrix.</param>
/// <param name="Model">The exact model identifier exercised.</param>
/// <param name="EndpointHost">
/// Host only. The full URL can carry a resource name or a query string; the host is enough to
/// tell an international endpoint from a mainland one, which is all a reader needs.
/// </param>
/// <param name="OrkeonVersion">The Orkeon version under test.</param>
/// <param name="Commit">Short commit of the tree under test, supplied by the caller.</param>
/// <param name="TimestampUtc">Campaign timestamp, supplied by the caller.</param>
/// <param name="Temperature">
/// The sampling temperature the run pinned. Recorded because it decides whether a verdict is
/// reproducible: at the framework default of 0.7 the same probe returned ❌ ✅ ❌ on three
/// consecutive Ollama runs (2026-08-01), which is a coin toss, not a measurement.
/// </param>
/// <param name="ThinkingEffort">
/// Base reasoning-effort hint the run pinned, or null when none was. Recorded for the same
/// reason as the temperature: `gpt-5.6-sol` refuses function tools on chat/completions
/// unless reasoning is explicitly off (2026-08-30), so a verdict obtained with
/// <c>none</c> must say so or it reads as the model's default behaviour.
/// </param>
/// <param name="M7ThinkingEffort">
/// Effort M7 probed with, recorded only when it is not the default <c>low</c>
/// (<c>mistral-medium-2604</c> accepts only <c>high</c> or <c>none</c>, 2026-08-30). The
/// per-model registry promises the header prints the values actually used — this is that
/// promise for M7.
/// </param>
internal sealed record LlmProbeContext(
    string Provider,
    string Model,
    string EndpointHost,
    string OrkeonVersion,
    string Commit,
    DateTimeOffset TimestampUtc,
    double Temperature,
    string? ThinkingEffort = null,
    string? M7ThinkingEffort = null);

/// <summary>
/// Renders a campaign into the two shapes a run needs: one for a human reading the terminal,
/// one for the campaign scripts that build the per-provider report and the index.
/// </summary>
/// <remarks>
/// Rendering lives apart from <see cref="LlmProbeRunner"/> on purpose. The runner encodes the
/// protocol, which changes when the matrix does; the report format changes whenever someone
/// wants a different column. Keeping them together would tie a cosmetic edit to the file that
/// defines what "tested" means.
/// </remarks>
internal static class LlmProbeReport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Resolves the Orkeon version, preferring the informational form (<c>0.9.2-beta</c>).</summary>
    public static string ResolveVersion()
    {
        var assembly = typeof(LlmProbeReport).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
            return assembly.GetName().Version?.ToString() ?? "unknown";

        // SourceLink appends "+<sha>"; the sha is reported separately and would be noise here.
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? informational : informational[..plus];
    }

    /// <summary>Renders the campaign as a Markdown fragment shaped like the matrix journal (§7).</summary>
    /// <param name="context">Identity of the run.</param>
    /// <param name="results">The per-mode results.</param>
    /// <returns>A Markdown fragment ready to read or paste.</returns>
    public static string ToMarkdown(LlmProbeContext context, IReadOnlyList<LlmProbeResult> results)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(results);

        var sb = new StringBuilder();
        sb.Append("# Campagne ").Append(context.Provider).Append(" — ").Append(context.Model).AppendLine();
        sb.AppendLine();
        sb.Append("- **Horodatage (UTC)** : ")
          .AppendLine(context.TimestampUtc.ToString("u", CultureInfo.InvariantCulture));
        sb.Append("- **Version Orkeon** : ").AppendLine(context.OrkeonVersion);
        sb.Append("- **Commit** : ").AppendLine(string.IsNullOrWhiteSpace(context.Commit) ? "non fourni" : context.Commit);
        sb.Append("- **Endpoint** : ").AppendLine(context.EndpointHost);
        sb.Append("- **Température** : ")
          .AppendLine(context.Temperature.ToString("0.##", CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(context.ThinkingEffort))
            sb.Append("- **Effort de raisonnement (base)** : ").AppendLine(context.ThinkingEffort);
        if (!string.IsNullOrWhiteSpace(context.M7ThinkingEffort))
            sb.Append("- **Effort de raisonnement (M7)** : ").AppendLine(context.M7ThinkingEffort);
        sb.AppendLine("- **Qualité de preuve** : sortie archivée");
        sb.AppendLine();
        sb.AppendLine("| Mode | Résultat | Détail | Durée |");
        sb.AppendLine("|---|---|---|---|");

        foreach (var result in results)
        {
            sb.Append("| ").Append(result.Mode)
              .Append(" | ").Append(Symbol(result.Outcome))
              .Append(" | ").Append(result.Detail.Replace('|', '/'))
              .Append(" | ").Append(result.ElapsedMs.ToString(CultureInfo.InvariantCulture)).AppendLine(" ms |");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Renders the campaign as JSON for the scripts that assemble the archived report and the
    /// index. Contains nothing the caller did not already know — in particular, no API key.
    /// </summary>
    /// <param name="context">Identity of the run.</param>
    /// <param name="results">The per-mode results.</param>
    /// <returns>Indented JSON.</returns>
    public static string ToJson(LlmProbeContext context, IReadOnlyList<LlmProbeResult> results)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(results);

        var payload = new
        {
            provider = context.Provider,
            model = context.Model,
            endpointHost = context.EndpointHost,
            orkeonVersion = context.OrkeonVersion,
            commit = context.Commit,
            timestampUtc = context.TimestampUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            temperature = context.Temperature,
            thinking_effort = context.ThinkingEffort,
            m7_thinking_effort = context.M7ThinkingEffort,
            passed = results.Count(r => r.Outcome == LlmProbeOutcome.Passed),
            failed = results.Count(r => r.Outcome == LlmProbeOutcome.Failed),
            notApplicable = results.Count(r => r.Outcome == LlmProbeOutcome.NotApplicable),
            modes = results.Select(r => new
            {
                mode = r.Mode.ToString(),
                outcome = OutcomeName(r.Outcome),
                symbol = Symbol(r.Outcome),
                detail = r.Detail,
                elapsedMs = r.ElapsedMs,
            }),
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string Symbol(LlmProbeOutcome outcome) => outcome switch
    {
        LlmProbeOutcome.Passed => "✅",
        LlmProbeOutcome.Failed => "❌",
        _ => "➖",
    };

    private static string OutcomeName(LlmProbeOutcome outcome) => outcome switch
    {
        LlmProbeOutcome.Passed => "passed",
        LlmProbeOutcome.Failed => "failed",
        _ => "not-applicable",
    };
}
