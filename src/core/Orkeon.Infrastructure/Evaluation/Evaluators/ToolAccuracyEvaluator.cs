using Orkeon.Application.Evaluation;
using Orkeon.Infrastructure.Evaluation.Base;

namespace Orkeon.Infrastructure.Evaluation.Evaluators;

/// <summary>
/// Typed input for tool accuracy evaluation.
/// </summary>
public sealed record ToolAccuracyInput
{
    /// <summary>Gets the output text to check for tool mentions.</summary>
    public string Output { get; init; } = string.Empty;

    /// <summary>Gets the comma-separated list of expected tool names.</summary>
    public string ExpectedTools { get; init; } = string.Empty;
}

/// <summary>
/// Typed result for tool accuracy evaluation.
/// </summary>
public sealed record ToolAccuracyResult
{
    /// <summary>Gets the number of expected tools found in the output.</summary>
    public int Found { get; init; }

    /// <summary>Gets the total number of expected tools.</summary>
    public int Total { get; init; }

    /// <summary>Gets the accuracy score as a fraction (0.0 to 1.0).</summary>
    public double Score { get; init; }

    /// <summary>Gets the human-readable reasoning for the score.</summary>
    public string Reasoning { get; init; } = string.Empty;

    /// <summary>Gets a map of tool names to found/missing status.</summary>
    public Dictionary<string, object> ToolDetails { get; init; } = [];
}

/// <summary>
/// Checks whether expected tool calls appear in the output text.
/// The input metadata should contain "expected_tools" as a comma-separated list of tool names.
/// Score is the fraction of expected tools that are mentioned in the output.
/// </summary>
public sealed class ToolAccuracyEvaluator : EvaluatorBase<ToolAccuracyInput, ToolAccuracyResult>
{
    /// <inheritdoc />
    public override string Name => "ToolAccuracy";

    /// <inheritdoc />
    public override string Description => "Checks if expected tool calls are mentioned in the output.";

    /// <summary>
    /// Metadata key for expected tools (comma-separated list).
    /// </summary>
    public const string ExpectedToolsKey = "expected_tools";

    /// <inheritdoc />
    protected override Dictionary<string, object?> BuildParametersFromInput(EvaluationInput input)
    {
        var toolsCsv = input.Metadata?.GetValueOrDefault(ExpectedToolsKey);
        return new Dictionary<string, object?>
        {
            ["output"] = input.Output,
            ["expected_tools"] = toolsCsv ?? string.Empty
        };
    }

    /// <inheritdoc />
    protected override Task<ToolAccuracyResult> ExecuteTypedAsync(ToolAccuracyInput request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ExpectedTools))
        {
            return Task.FromResult(new ToolAccuracyResult
            {
                Score = 1.0,
                Reasoning = "No expected tools specified; skipping check."
            });
        }

        var expectedTools = request.ExpectedTools
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        if (expectedTools.Length == 0)
        {
            return Task.FromResult(new ToolAccuracyResult
            {
                Score = 1.0,
                Reasoning = "Empty expected tools list."
            });
        }

        var output = request.Output;
        var found = 0;
        var toolDetails = new Dictionary<string, object>();

        foreach (var tool in expectedTools)
        {
            var present = output.Contains(tool, StringComparison.OrdinalIgnoreCase);
            toolDetails[tool] = present ? "found" : "missing";
            if (present) found++;
        }

        var score = (double)found / expectedTools.Length;
        var reasoning = $"{found}/{expectedTools.Length} expected tools found in output.";

        return Task.FromResult(new ToolAccuracyResult
        {
            Found = found,
            Total = expectedTools.Length,
            Score = score,
            Reasoning = reasoning,
            ToolDetails = toolDetails
        });
    }

    /// <inheritdoc />
    protected override double ExtractScore(ToolAccuracyResult result) => result.Score;

    /// <inheritdoc />
    protected override string? ExtractReasoning(ToolAccuracyResult result) => result.Reasoning;
}
