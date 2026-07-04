using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock for IOutputValidationPipeline with call tracking and configurable results.
/// </summary>
public class MockOutputValidationPipeline : IOutputValidationPipeline
{
    private OutputPipelineResult _validateResult = new(true, Array.Empty<OutputValidationResult>());
    private readonly List<IOutputValidator> _validators = [];

    // --- Tracking ---
    public int ValidateCallCount { get; private set; }
    public string? LastValidatedOutput { get; private set; }
    public OutputValidationContext? LastValidationContext { get; private set; }

    public int AddValidatorCallCount { get; private set; }
    public IOutputValidator? LastAddedValidator { get; private set; }

    // --- Configuration ---
    public void SetValidateResult(OutputPipelineResult result) => _validateResult = result;

    public void SetValid() =>
        _validateResult = new OutputPipelineResult(true, Array.Empty<OutputValidationResult>());

    public void SetInvalid(string errorMessage) =>
        _validateResult = new OutputPipelineResult(
            false,
            [new OutputValidationResult(false, errorMessage)],
            errorMessage);

    // --- IOutputValidationPipeline ---
    public Task<OutputPipelineResult> ValidateAsync(
        string output,
        OutputValidationContext context,
        CancellationToken ct = default)
    {
        ValidateCallCount++;
        LastValidatedOutput = output;
        LastValidationContext = context;
        return Task.FromResult(_validateResult);
    }

    public IOutputValidationPipeline AddValidator(IOutputValidator validator)
    {
        AddValidatorCallCount++;
        LastAddedValidator = validator;
        _validators.Add(validator);
        return this;
    }
}
