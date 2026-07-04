using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task;

namespace Orkeon.Domain.Common;

/// <summary>
/// Static factory providing entry points for the fluent builder API.
/// </summary>
public static class FluentBuilderFactory
{
    /// <summary>
    /// Creates an <see cref="AgentBuilder"/> pre-configured with the specified role and goal.
    /// </summary>
    /// <param name="role">The agent role.</param>
    /// <param name="goal">The agent goal.</param>
    /// <returns>A pre-configured <see cref="AgentBuilder"/>.</returns>
    public static AgentBuilder BuildAgent(string role, string goal)
        => new AgentBuilder().Role(role).Goal(goal);

    /// <summary>
    /// Creates a <see cref="CrewTaskBuilder"/> pre-configured with the specified description and expected output.
    /// </summary>
    /// <param name="description">The task description.</param>
    /// <param name="expectedOutput">The expected output.</param>
    /// <returns>A pre-configured <see cref="CrewTaskBuilder"/>.</returns>
    public static CrewTaskBuilder BuildTask(string description, string expectedOutput)
        => new CrewTaskBuilder().Description(description).ExpectedOutput(expectedOutput);

    /// <summary>
    /// Creates a <see cref="CrewBuilder"/> pre-configured with the specified goal.
    /// </summary>
    /// <param name="goal">The crew goal.</param>
    /// <returns>A pre-configured <see cref="CrewBuilder"/>.</returns>
    public static CrewBuilder BuildCrew(string goal)
        => new CrewBuilder().Goal(goal);
}
