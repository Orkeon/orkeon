using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Application.Context;

/// <summary>
/// No-op memory scope for when memory is not configured.
/// Thread-safe, stateless, reusable singleton.
/// </summary>
public sealed class NullMemoryScope : IMemoryScope
{
    /// <summary>Singleton instance.</summary>
    public static readonly NullMemoryScope Instance = new();

    /// <inheritdoc />
    public string AgentId => "none";

    /// <inheritdoc />
    public string ScopeId => "null-scope";

    /// <inheritdoc />
    public System.Threading.Tasks.Task<T> ExecuteInScopeAsync<T>(Func<System.Threading.Tasks.Task<T>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        return operation();
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task ExecuteInScopeAsync(Func<System.Threading.Tasks.Task> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        return operation();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
