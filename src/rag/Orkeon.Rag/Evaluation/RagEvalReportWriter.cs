using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Domain.FileSystem;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Evaluation;

/// <summary>
/// Serializes evaluation reports to markdown and JSON, and writes them through
/// the virtual file system (default output root <c>/output/rag/eval</c>). File
/// names are stable (<c>{dataset}-{profile}.md|.json</c>, comparison
/// <c>{dataset}-compare.md</c>) so re-runs overwrite in place.
/// </summary>
public sealed class RagEvalReportWriter
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly IFileSystemService _fileSystem;

    /// <summary>Initializes the writer over the virtual file system.</summary>
    public RagEvalReportWriter(IFileSystemService fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Writes the markdown and JSON renditions of <paramref name="report"/> under
    /// <paramref name="outputDirectory"/>; returns the two virtual paths written.
    /// </summary>
    public async Task<IReadOnlyList<string>> WriteAsync(
        RagEvalReport report, string outputDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        var baseName = $"{Slug(report.DatasetName)}-{Slug(report.Profile)}";
        var markdownPath = $"{outputDirectory.TrimEnd('/')}/{baseName}.md";
        var jsonPath = $"{outputDirectory.TrimEnd('/')}/{baseName}.json";

        await _fileSystem.WriteAllTextAsync(markdownPath, ToMarkdown(report), cancellationToken)
            .ConfigureAwait(false);
        await _fileSystem.WriteAllTextAsync(jsonPath, ToJson(report), cancellationToken)
            .ConfigureAwait(false);

        return [markdownPath, jsonPath];
    }

    /// <summary>
    /// Writes the multi-profile comparison table (<c>--compare</c>, plan §9.3);
    /// returns the virtual path written.
    /// </summary>
    public async Task<string> WriteComparisonAsync(
        IReadOnlyList<RagEvalReport> reports, string outputDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        if (reports.Count == 0)
            throw new ArgumentException("At least one report is required.", nameof(reports));

        var path = $"{outputDirectory.TrimEnd('/')}/{Slug(reports[0].DatasetName)}-compare.md";
        await _fileSystem.WriteAllTextAsync(path, ToComparisonMarkdown(reports), cancellationToken)
            .ConfigureAwait(false);
        return path;
    }

    /// <summary>Renders a single-run report as markdown.</summary>
    public static string ToMarkdown(RagEvalReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var sb = new StringBuilder();
        sb.Append("# RAG evaluation — dataset `").Append(report.DatasetName)
          .Append("`, profile `").Append(report.Profile).AppendLine("`");
        sb.AppendLine();
        sb.Append("- Judge: ").AppendLine(JudgeLabel(report));
        sb.Append("- Collection: `").Append(report.Collection).AppendLine("`");
        sb.AppendLine(Invariant($"- K: {report.K} — cases: {report.Aggregate.CaseCount} — duration: {report.Duration.TotalSeconds:F2}s"));
        sb.AppendLine(Invariant($"- Started (UTC): {report.StartedAt:yyyy-MM-dd HH:mm:ss}"));
        sb.AppendLine();

        sb.AppendLine(Invariant($"| case | recall@{report.K} | precision@{report.K} | RR | groundedness | answer-relevance | judge | ms | tags |"));
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var c in report.Cases)
        {
            sb.Append("| ").Append(c.CaseId)
              .Append(" | ").Append(Metric(c.RecallAtK))
              .Append(" | ").Append(Metric(c.PrecisionAtK))
              .Append(" | ").Append(Metric(c.ReciprocalRank))
              .Append(" | ").Append(Metric(c.Groundedness))
              .Append(" | ").Append(Metric(c.AnswerRelevance))
              .Append(" | ").Append(ModeLabel(c.Judge))
              .Append(" | ").Append(((long)c.Duration.TotalMilliseconds).ToString(CultureInfo.InvariantCulture))
              .Append(" | ").Append(string.Join(", ", c.Tags))
              .AppendLine(" |");
        }

        sb.AppendLine();
        sb.AppendLine("## Aggregate");
        sb.AppendLine();
        sb.AppendLine(Invariant($"| recall@{report.K} | precision@{report.K} | MRR | groundedness | answer-relevance |"));
        sb.AppendLine("|---|---|---|---|---|");
        sb.Append("| ").Append(Metric(report.Aggregate.RecallAtK))
          .Append(" | ").Append(Metric(report.Aggregate.PrecisionAtK))
          .Append(" | ").Append(Metric(report.Aggregate.Mrr))
          .Append(" | ").Append(Metric(report.Aggregate.Groundedness))
          .Append(" | ").Append(Metric(report.Aggregate.AnswerRelevance))
          .AppendLine(" |");

        return sb.ToString();
    }

    /// <summary>Renders the multi-profile comparison table as markdown (one row per profile).</summary>
    public static string ToComparisonMarkdown(IReadOnlyList<RagEvalReport> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);
        if (reports.Count == 0)
            throw new ArgumentException("At least one report is required.", nameof(reports));

        var k = reports[0].K;
        var sb = new StringBuilder();
        sb.Append("# RAG evaluation — profile comparison (dataset `")
          .Append(reports[0].DatasetName).AppendLine("`)");
        sb.AppendLine();
        sb.AppendLine(Invariant($"| profile | recall@{k} | MRR | groundedness | answer-relevance | judge | ms/case |"));
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var report in reports)
        {
            var meanMs = report.Cases.Count > 0
                ? report.Cases.Average(c => c.Duration.TotalMilliseconds)
                : 0.0;
            sb.Append("| ").Append(report.Profile)
              .Append(" | ").Append(Metric(report.Aggregate.RecallAtK))
              .Append(" | ").Append(Metric(report.Aggregate.Mrr))
              .Append(" | ").Append(Metric(report.Aggregate.Groundedness))
              .Append(" | ").Append(Metric(report.Aggregate.AnswerRelevance))
              .Append(" | ").Append(JudgeLabel(report))
              .Append(" | ").Append(meanMs.ToString("F0", CultureInfo.InvariantCulture))
              .AppendLine(" |");
        }

        return sb.ToString();
    }

    /// <summary>Serializes a report as indented snake_case JSON (enums as strings).</summary>
    public static string ToJson(RagEvalReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, s_jsonOptions);
    }

    /// <summary>Human label of the judge mode, with the fallback count when relevant.</summary>
    public static string JudgeLabel(RagEvalReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var label = ModeLabel(report.Judge);
        return report is { Judge: RagJudgeMode.Llm, JudgeFallbackCount: > 0 }
            ? Invariant($"{label} ({report.JudgeFallbackCount} heuristic fallback(s))")
            : label;
    }

    private static string ModeLabel(RagJudgeMode mode)
        => mode == RagJudgeMode.Llm ? "llm" : "heuristic";

    private static string Metric(double? value)
        => value.HasValue ? value.Value.ToString("F2", CultureInfo.InvariantCulture) : "n/a";

    private static string Invariant(FormattableString value)
        => value.ToString(CultureInfo.InvariantCulture);

    private static string Slug(string value)
    {
        var sb = new StringBuilder(value.Length);
#pragma warning disable CA1308 // lowercase is the file-name form, not a comparison normalization
        var lowered = value.Trim().ToLowerInvariant();
#pragma warning restore CA1308
        foreach (var ch in lowered)
            sb.Append(char.IsAsciiLetterOrDigit(ch) ? ch : '-');
        var slug = sb.ToString().Trim('-');
        return slug.Length > 0 ? slug : "dataset";
    }
}
