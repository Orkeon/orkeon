using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.OutputParsing.Validation;

/// <summary>
/// Validates that output length falls within the specified min/max constraints.
/// Priority: 5 (runs very early).
/// </summary>
public sealed class LengthValidator : IOutputValidator
{
    /// <inheritdoc />
    public string Name => "LengthValidator";

    /// <inheritdoc />
    public int Priority => 5;

    /// <inheritdoc />
    public Task<OutputValidationResult> ValidateAsync(
        string output,
        OutputValidationContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(context);
        if (context.MinLength.HasValue && output.Length < context.MinLength.Value)
        {
            return Task.FromResult(new OutputValidationResult(
                IsValid: false,
                ErrorMessage: $"Output is too short ({output.Length} characters). Minimum required: {context.MinLength.Value}.",
                SuggestedFix: $"Please provide a more detailed response with at least {context.MinLength.Value} characters."));
        }

        if (context.MaxLength.HasValue && output.Length > context.MaxLength.Value)
        {
            return Task.FromResult(new OutputValidationResult(
                IsValid: false,
                ErrorMessage: $"Output is too long ({output.Length} characters). Maximum allowed: {context.MaxLength.Value}.",
                SuggestedFix: $"Please shorten your response to at most {context.MaxLength.Value} characters."));
        }

        return Task.FromResult(new OutputValidationResult(IsValid: true));
    }
}
