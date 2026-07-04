namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// Pipeline that chains multiple output validators together.
/// </summary>
public interface IOutputValidationPipeline
{
    /// <summary>
    /// Validates the output by running all registered validators in priority order.
    /// </summary>
    System.Threading.Tasks.Task<OutputPipelineResult> ValidateAsync(
        string output,
        OutputValidationContext context,
        CancellationToken ct = default);

    /// <summary>
    /// Adds a validator to the pipeline.
    /// </summary>
    IOutputValidationPipeline AddValidator(IOutputValidator validator);
}

/// <summary>
/// Combined result from all validators in the pipeline.
/// </summary>
public record OutputPipelineResult(
    bool IsValid,
    IReadOnlyList<OutputValidationResult> Results,
    string? CombinedErrorMessage = null);
