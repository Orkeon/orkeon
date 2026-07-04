
namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// Provides memory isolation scope for individual agents.
/// Ensures that memory operations are thread-safe and isolated per agent.
/// </summary>
public interface IMemoryScope : IDisposable
{
    /// <summary>
    /// Gets the agent identifier for this scope.
    /// </summary>
    string AgentId { get; }

    /// <summary>
    /// Gets the unique scope identifier.
    /// </summary>
    string ScopeId { get; }

    /// <summary>
    /// Executes an async operation within the agent's memory scope.
    /// </summary>
    /// <typeparam name="T">The return type of the operation</typeparam>
    /// <param name="operation">The async operation to execute</param>
    /// <returns>The result of the operation</returns>
    System.Threading.Tasks.Task<T> ExecuteInScopeAsync<T>(Func<System.Threading.Tasks.Task<T>> operation);

    /// <summary>
    /// Executes an async operation within the agent's memory scope.
    /// </summary>
    /// <param name="operation">The async operation to execute</param>
    System.Threading.Tasks.Task ExecuteInScopeAsync(Func<System.Threading.Tasks.Task> operation);
}

/// <summary>
/// Factory for creating memory scopes per agent.
/// Paired with <see cref="IMemoryScope"/>; implementations should be registered
/// in the DI container when per-agent memory isolation is required.
/// </summary>
/// <remarks>
/// [ARCHITECTURE] This interface is intentionally kept even though no implementation
/// exists yet. It completes the factory pattern for <see cref="IMemoryScope"/> and
/// will be implemented when multi-agent memory isolation is needed (Phase 2+).
/// </remarks>
public interface IMemoryScopeFactory
{
    /// <summary>
    /// Creates a new memory scope for the specified agent.
    /// </summary>
    /// <param name="agentId">The agent identifier</param>
    /// <returns>A new memory scope</returns>
    IMemoryScope CreateScope(string agentId);
}
