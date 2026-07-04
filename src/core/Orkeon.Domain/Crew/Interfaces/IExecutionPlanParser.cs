using Orkeon.Domain.Common;

namespace Orkeon.Domain.Crew.Interfaces;

/// <summary>
/// Abstraction for parsing LLM responses into structured ExecutionPlan objects.
/// Allows different parsing strategies (JSON, text, hybrid) without coupling
/// the Domain layer to any serialization technology.
/// </summary>
public interface IExecutionPlanParser
{
    /// <summary>
    /// Attempts to parse an LLM response into an ExecutionPlan.
    /// </summary>
    /// <param name="llmResponse">The raw LLM response (JSON or text).</param>
    /// <param name="tasks">Available task IDs for validation.</param>
    /// <param name="agents">Available agent IDs for validation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A parsed ExecutionPlan, or <see langword="null"/> if parsing fails.</returns>
    Task<ExecutionPlan?> ParseAsync(
        string llmResponse,
        IReadOnlyList<TaskId> tasks,
        IReadOnlyList<AgentId> agents,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the name/type of this parser (for logging/debugging).
    /// </summary>
    string ParserName { get; }
}
