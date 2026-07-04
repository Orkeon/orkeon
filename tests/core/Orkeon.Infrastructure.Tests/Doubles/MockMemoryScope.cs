using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock implementation of IMemoryScope for testing.
/// </summary>
public sealed class MockMemoryScope : IMemoryScope
{
    // --- Tracking ---
    public int ExecuteInScopeAsyncCallCount { get; private set; }
    public int DisposeCallCount { get; private set; }

    public string AgentId { get; set; } = "mock-agent-id";
    public string ScopeId { get; set; } = Guid.NewGuid().ToString();

    public async Task<T> ExecuteInScopeAsync<T>(Func<Task<T>> operation)
    {
        ExecuteInScopeAsyncCallCount++;
        return await operation();
    }

    public async Task ExecuteInScopeAsync(Func<Task> operation)
    {
        ExecuteInScopeAsyncCallCount++;
        await operation();
    }

    public void Dispose()
    {
        DisposeCallCount++;
    }

    /// <summary>
    /// Resets all tracking counters.
    /// </summary>
    public void Reset()
    {
        ExecuteInScopeAsyncCallCount = 0;
        DisposeCallCount = 0;
    }
}
