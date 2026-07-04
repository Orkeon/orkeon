using System.Globalization;
using System.Text;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.OutputParsing;

/// <summary>
/// Handles output validation and builds correction prompts for LLM retry.
/// </summary>
public sealed class OutputRetryHandler
{
    private readonly IOutputValidationPipeline _pipeline;
    private readonly int _maxRetries;

    /// <summary>Initializes a new instance of <see cref="OutputRetryHandler"/>.</summary>
    /// <param name="pipeline">The validation pipeline to use.</param>
    /// <param name="maxRetries">Maximum number of retries allowed.</param>
    public OutputRetryHandler(IOutputValidationPipeline pipeline, int maxRetries = 2)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        _pipeline = pipeline;
        _maxRetries = maxRetries;
    }

    /// <summary>
    /// Gets the maximum number of retries configured.
    /// </summary>
    public int MaxRetries => _maxRetries;

    /// <summary>
    /// Builds a correction prompt that tells the LLM what went wrong and how to fix it.
    /// </summary>
    public static string BuildCorrectionPrompt(
        string originalOutput,
        OutputPipelineResult validationResult,
        OutputValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(validationResult);
        ArgumentNullException.ThrowIfNull(context);
        var sb = new StringBuilder();
        sb.AppendLine("Your previous output had validation errors:");

        foreach (var result in validationResult.Results.Where(r => !r.IsValid))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"- [{result.ValidatorName}] {result.ErrorMessage}");
            if (!string.IsNullOrEmpty(result.SuggestedFix))
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Fix: {result.SuggestedFix}");
            }
        }

        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Please fix these issues and respond again in {context.ExpectedFormat} format.");

        if (context.RequiredFields?.Count > 0)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Required fields: {string.Join(", ", context.RequiredFields)}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Validates the output and returns either a success or a retry result with a correction prompt.
    /// </summary>
    public async Task<OutputRetryResult> ValidateOrRetryAsync(
        string output,
        OutputValidationContext context,
        CancellationToken ct = default)
    {
        var result = await _pipeline.ValidateAsync(output, context, ct).ConfigureAwait(false);

        if (result.IsValid)
            return OutputRetryResult.Success(output);

        return OutputRetryResult.NeedsRetry(
            BuildCorrectionPrompt(output, result, context));
    }
}

/// <summary>
/// Result of a validation-or-retry check.
/// </summary>
public record OutputRetryResult(bool IsValid, string? CorrectionPrompt, string Output)
{
    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static OutputRetryResult Success(string output) => new(true, null, output);

    /// <summary>
    /// Creates a result indicating the output needs correction.
    /// </summary>
    public static OutputRetryResult NeedsRetry(string correctionPrompt) => new(false, correctionPrompt, "");
}
