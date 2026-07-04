using Orkeon.Domain.Common;

namespace Orkeon.Domain.Flows;

/// <summary>
/// Interface for defining flow structures.
/// </summary>
public interface IFlowDefinition
{
    /// <summary>
    /// Gets the flow identifier.
    /// </summary>
    FlowId Id { get; }

    /// <summary>
    /// Gets the flow name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the flow description.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets the flow type.
    /// </summary>
    FlowType Type { get; }

    /// <summary>
    /// Gets the steps in the flow.
    /// </summary>
    IReadOnlyList<FlowStep> Steps { get; }

    /// <summary>
    /// Gets the flow configuration.
    /// </summary>
    FlowConfiguration Configuration { get; }

    /// <summary>
    /// Validates the flow definition.
    /// </summary>
    bool Validate(out IReadOnlyList<string> errors);
}
