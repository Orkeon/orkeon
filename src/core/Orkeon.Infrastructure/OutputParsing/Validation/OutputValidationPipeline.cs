using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.OutputParsing.Validation;

/// <summary>
/// Chains multiple output validators together, running them in priority order
/// and collecting all results.
/// </summary>
public sealed class OutputValidationPipeline : IOutputValidationPipeline
{
    private readonly List<IOutputValidator> _validators = [];

    /// <summary>Initializes a new instance of <see cref="OutputValidationPipeline"/> with no initial validators.</summary>
    public OutputValidationPipeline()
    {
    }

    /// <summary>Initializes a new instance of <see cref="OutputValidationPipeline"/> pre-loaded with the given validators.</summary>
    /// <param name="validators">The validators to add to the pipeline.</param>
    public OutputValidationPipeline(IEnumerable<IOutputValidator> validators)
    {
        _validators.AddRange(validators);
    }

    /// <inheritdoc/>
    public IOutputValidationPipeline AddValidator(IOutputValidator validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        _validators.Add(validator);
        return this;
    }

    /// <inheritdoc/>
    public async Task<OutputPipelineResult> ValidateAsync(
        string output,
        OutputValidationContext context,
        CancellationToken ct = default)
    {
        var results = new List<OutputValidationResult>();
        var sortedValidators = _validators.OrderBy(v => v.Priority).ToList();

        foreach (var validator in sortedValidators)
        {
            ct.ThrowIfCancellationRequested();
            var result = await validator.ValidateAsync(output, context, ct).ConfigureAwait(false);
            results.Add(result with { ValidatorName = validator.Name });
        }

        var isValid = results.All(r => r.IsValid);
        var errors = results
            .Where(r => !r.IsValid && r.ErrorMessage != null)
            .Select(r => r.ErrorMessage!)
            .ToList();

        var combinedError = errors.Count > 0
            ? string.Join("; ", errors)
            : null;

        return new OutputPipelineResult(isValid, results, combinedError);
    }
}
