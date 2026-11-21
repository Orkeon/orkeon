using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Application.Tests.Doubles;

/// <summary><see cref="IOutputValidationPipeline"/> that accepts every output, after rejecting the first <see cref="RejectFirst"/>.</summary>
internal sealed class StubOutputValidationPipeline : IOutputValidationPipeline
{
    /// <summary>How many outputs are rejected before every other one is accepted; none by default.</summary>
    public int RejectFirst { get; set; }

    public System.Threading.Tasks.Task<OutputPipelineResult> ValidateAsync(
        string output, OutputValidationContext context, CancellationToken ct = default)
    {
        if (RejectFirst <= 0)
            return System.Threading.Tasks.Task.FromResult(new OutputPipelineResult(true, [new OutputValidationResult(true)]));

        RejectFirst--;
        return System.Threading.Tasks.Task.FromResult(
            new OutputPipelineResult(false, [new OutputValidationResult(false, "The output is not the JSON the task expects.")]));
    }

    public IOutputValidationPipeline AddValidator(IOutputValidator validator) => this;
}
