using Orkeon.Domain.Flows;

namespace Orkeon.Application.Interfaces;

/// <summary>
/// Resolves a FlowStep definition into an executable IFlowStep instance.
/// </summary>
public interface IFlowStepExecutor
{
    /// <summary>
    /// Resolves a step definition into an executable flow step.
    /// </summary>
    /// <param name="stepDefinition">The flow step definition to resolve.</param>
    /// <returns>An executable flow step instance.</returns>
    IFlowStep ResolveStep(FlowStep stepDefinition);
}
