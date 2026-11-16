using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Application.Tests.Doubles;

/// <summary><see cref="IOutputValidationPipeline"/> that accepts every output.</summary>
internal sealed class StubOutputValidationPipeline : IOutputValidationPipeline
{
    public System.Threading.Tasks.Task<OutputPipelineResult> ValidateAsync(
        string output, OutputValidationContext context, CancellationToken ct = default) =>
        System.Threading.Tasks.Task.FromResult(new OutputPipelineResult(true, [new OutputValidationResult(true)]));

    public IOutputValidationPipeline AddValidator(IOutputValidator validator) => this;
}
