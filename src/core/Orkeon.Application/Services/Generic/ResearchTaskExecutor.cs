using Microsoft.Extensions.Logging;
using Orkeon.Application.Rag;
using Orkeon.Application.Execution;
using Orkeon.Domain.Task;
using Orkeon.Domain.Constants.Orchestration;

namespace Orkeon.Application.Services.Generic;

/// <summary>
/// Research task context with typed data.
/// </summary>
public class ResearchTaskContext
{
    private readonly List<string> _sources = [];
    private readonly List<string> _keyFindings = [];

    /// <summary>Gets the topic.</summary>
    public string Topic { get; init; } = string.Empty;
    /// <summary>Gets the sources.</summary>
    public IReadOnlyList<string> Sources => _sources;
    /// <summary>Relevance Scores.</summary>
    public Dictionary<string, float> RelevanceScores { get; init; } = [];
    /// <summary>Gets the key findings.</summary>
    public IReadOnlyList<string> KeyFindings => _keyFindings;

    /// <summary>Replaces the sources with the supplied values.</summary>
    public void SetSources(IEnumerable<string> sources)
    {
        _sources.Clear();
        _sources.AddRange(sources);
    }

    /// <summary>Replaces the key findings with the supplied values.</summary>
    public void SetKeyFindings(IEnumerable<string> keyFindings)
    {
        _keyFindings.Clear();
        _keyFindings.AddRange(keyFindings);
    }

    /// <summary>Gets the research started.</summary>
    public DateTime ResearchStarted { get; init; } = DateTime.UtcNow;
    /// <summary>Gets the max sources.</summary>
    public int MaxSources { get; init; } = 10;
    /// <summary>Gets the max duration.</summary>
    public TimeSpan MaxDuration { get; init; } = OrchestrationDefaults.DefaultResearchMaxDuration;
}

/// <summary>
/// Research task result with structured output.
/// </summary>
public class ResearchTaskResult
{
    /// <summary>Gets or sets the summary.</summary>
    public string Summary { get; set; } = string.Empty;
    /// <summary>Gets or sets the sources used.</summary>
    public IReadOnlyList<string> SourcesUsed { get; init; } = [];
    /// <summary>Gets or sets the findings.</summary>
    public ResearchFindings Findings { get; set; } = ResearchFindings.Empty;
    /// <summary>Gets or sets the confidence score.</summary>
    public float ConfidenceScore { get; set; }
    /// <summary>Gets or sets the actual duration.</summary>
    public TimeSpan ActualDuration { get; set; }
    /// <summary>
    /// Gets or sets a value indicating whether success.
    /// </summary>
    public bool Success { get; set; }
    /// <summary>Gets or sets the error.</summary>
    public string? Error { get; set; }
}

/// <summary>
/// Specialized executor for research tasks.
/// Demonstrates typed task execution with domain-specific logic.
/// </summary>
public partial class ResearchTaskExecutor : TaskExecutorBase<ResearchTask, ResearchTaskResult>
{
    /// <summary>
    /// Initializes a new instance of <see cref="ResearchTaskExecutor"/>.
    /// </summary>
    public ResearchTaskExecutor(ILogger<ResearchTaskExecutor> logger)
        : base(logger)
    {
    }

    /// <summary>Validate Async(Research Task, I Execution Context).</summary>
    protected override async System.Threading.Tasks.Task<ValidationResult> ValidateAsync(
        ResearchTask task,
        IExecutionContext context)
    {
        var baseValidation = await base.ValidateAsync(task, context).ConfigureAwait(false);
        var errors = baseValidation.Errors.ToList();

        var typed = GetTypedContext<ResearchTaskContext>(context);

        // Research-specific validation
        if (string.IsNullOrWhiteSpace(typed.Data.Topic))
            errors.Add("Research topic is required");

        if (typed.Data.MaxSources <= 0)
            errors.Add("Max sources must be greater than 0");

        if (typed.Data.MaxDuration <= TimeSpan.Zero)
            errors.Add("Max duration must be positive");

        return new ValidationResult(errors);
    }

    /// <summary>Execute Core Async(Research Task, I Execution Context, Cancellation Token).</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Service-boundary fault barrier: any failure during research is converted to a failed ResearchTaskResult carrying the error so the executor returns a result instead of throwing to the pipeline.")]
    protected override async System.Threading.Tasks.Task<ResearchTaskResult> ExecuteCoreAsync(
        ResearchTask task,
        IExecutionContext context,
        CancellationToken cancellationToken)
    {
        var typed = GetTypedContext<ResearchTaskContext>(context);
        var startTime = DateTime.UtcNow;

        LogResearchStarting(typed.Data.Topic);

        try
        {
            // Simulate research process
            var sources = await GatherSourcesAsync(typed.Data, cancellationToken).ConfigureAwait(false);
            var findings = await AnalyzeSourcesAsync(sources, typed.Data, cancellationToken).ConfigureAwait(false);
            var summary = await GenerateSummaryAsync(findings, typed.Data, cancellationToken).ConfigureAwait(false);

            var duration = DateTime.UtcNow - startTime;

            return new ResearchTaskResult
            {
                Summary = summary,
                SourcesUsed = sources,
                Findings = findings,
                ConfidenceScore = CalculateConfidenceScore(findings, sources.Count),
                ActualDuration = duration,
                Success = true
            };
        }
        catch (Exception ex)
        {
            LogResearchFailed(ex, typed.Data.Topic);

            return new ResearchTaskResult
            {
                Success = false,
                Error = ex.Message,
                ActualDuration = DateTime.UtcNow - startTime
            };
        }
    }

    /// <summary>Post Process Async(Research Task, I Execution Context, Research Task Result).</summary>
    protected override System.Threading.Tasks.Task PostProcessAsync(
        ResearchTask task,
        IExecutionContext context,
        ResearchTaskResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return PostProcessCoreAsync();

        async System.Threading.Tasks.Task PostProcessCoreAsync()
        {
            var typed = GetTypedContext<ResearchTaskContext>(context);

            // Update context with results
            typed.UpdateData(data =>
            {
                data.SetKeyFindings(result.Findings.Sources);

                data.SetSources(result.SourcesUsed);

                // Extract relevance scores from findings
                data.RelevanceScores.Clear();
                foreach (var source in result.Findings.Sources)
                {
                    var finding = result.Findings.GetFinding(source);
                    if (finding is SourceAnalysis analysis)
                    {
                        data.RelevanceScores[source] = analysis.Relevance;
                    }
                    else
                    {
                        data.RelevanceScores[source] = 0.5f; // Default relevance
                    }
                }
            });

            LogResearchCompleted(typed.Data.Topic, result.ConfidenceScore);

            await base.PostProcessAsync(task, context, result).ConfigureAwait(false);
        }
    }

    private static async System.Threading.Tasks.Task<List<string>> GatherSourcesAsync(
        ResearchTaskContext context,
        CancellationToken cancellationToken)
    {
        // Simulate source gathering
        await System.Threading.Tasks.Task.Delay(1000, cancellationToken).ConfigureAwait(false);

        var sources = new List<string>();
        for (int i = 1; i <= Math.Min(context.MaxSources, 5); i++)
        {
            sources.Add(FormattableString.Invariant($"Source_{i}_for_{context.Topic.Replace(" ", "_", StringComparison.Ordinal)}"));
        }

        return sources;
    }

    private static async System.Threading.Tasks.Task<ResearchFindings> AnalyzeSourcesAsync(
        List<string> sources,
        ResearchTaskContext context,
        CancellationToken cancellationToken)
    {
        // Simulate source analysis
        await System.Threading.Tasks.Task.Delay(2000, cancellationToken).ConfigureAwait(false);

        var builder = ResearchFindings.CreateBuilder();
        foreach (var source in sources)
        {
            builder.AddSourceAnalysis(
                source,
#pragma warning disable CA5394 // simulated confidence score in the demo/simulation research executor; not security-sensitive.
                Random.Shared.NextSingle(),
#pragma warning restore CA5394
                [$"Point1_{source}", $"Point2_{source}"],
                $"Analysis of {source} regarding {context.Topic}"
            );
        }

        return builder.Build();
    }

    private static async System.Threading.Tasks.Task<string> GenerateSummaryAsync(
        ResearchFindings findings,
        ResearchTaskContext context,
        CancellationToken cancellationToken)
    {
        // Simulate summary generation
        await System.Threading.Tasks.Task.Delay(1000, cancellationToken).ConfigureAwait(false);

        return $"Research summary for '{context.Topic}': " +
               $"Analyzed {findings.Count} sources and found key insights. " +
               $"Research completed in {DateTime.UtcNow - context.ResearchStarted:mm\\:ss}.";
    }

    private static float CalculateConfidenceScore(ResearchFindings findings, int sourceCount)
    {
        // Simple confidence calculation based on source count and findings
        var baseConfidence = Math.Min(sourceCount / 10.0f, 1.0f);
        var findingsBonus = Math.Min(findings.Count / 20.0f, 0.3f);

        return Math.Min(baseConfidence + findingsBonus, 1.0f);
    }

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Starting research on topic: {Topic}")]
    private partial void LogResearchStarting(string topic);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Research failed for topic: {Topic}")]
    private partial void LogResearchFailed(Exception ex, string topic);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Research completed for topic {Topic} with confidence {Confidence:P}")]
    private partial void LogResearchCompleted(string topic, float confidence);
}

