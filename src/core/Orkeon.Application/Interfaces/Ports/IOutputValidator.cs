using AppOutputFormat = Orkeon.Application.Interfaces.Ports.OutputFormat;

namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// Validates LLM output against specific criteria.
/// </summary>
public interface IOutputValidator
{
    /// <summary>
    /// Gets the validator name for identification in results.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the priority order. Lower values run first.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Validates the given output against the provided context.
    /// </summary>
    System.Threading.Tasks.Task<OutputValidationResult> ValidateAsync(
        string output,
        OutputValidationContext context,
        CancellationToken ct = default);
}

/// <summary>
/// Result of a single validation check.
/// </summary>
public record OutputValidationResult(
    bool IsValid,
    string? ErrorMessage = null,
    string? SuggestedFix = null,
    string ValidatorName = "");

/// <summary>
/// Context describing what validation should check.
/// </summary>
public record OutputValidationContext(
    AppOutputFormat ExpectedFormat,
    Type? ExpectedType = null,
    Domain.Task.JsonSchema? Schema = null,
    int? MaxLength = null,
    int? MinLength = null,
    IReadOnlyList<string>? RequiredFields = null);
