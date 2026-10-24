using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Domain.Execution;

/// <summary>
/// The ambient scope of one agent run: what the agent creates while it runs (a shell session, a
/// child process) is registered here and released when the run ends, whether it succeeded, failed
/// or was cancelled. Modelled on <see cref="FileSystem.AsyncLocalFileSystemScope"/>: the current
/// scope lives in an <see cref="AsyncLocal{T}"/>, so concurrent agents never see each other's
/// scope, and a run started inside another run's flow (a delegated coworker) opens its own scope,
/// nested in its caller's, and restores the caller's when it ends.
/// </summary>
/// <remarks>
/// Resources are released in the reverse order of their registration. Ending never throws: a
/// resource whose disposal fails is recorded in <see cref="DisposalErrors"/> and the others are
/// still released. Once ended, the scope refuses new resources with an
/// <see cref="ObjectDisposedException"/>, so nothing is created after its agent stopped. Members
/// are thread-safe: tools of one agent may run concurrently.
/// </remarks>
public sealed class AgentRunScope : IAsyncDisposable
{
    private static readonly AsyncLocal<AgentRunScope?> s_current = new();

    private readonly object _sync = new();
    private readonly List<IAsyncDisposable> _resources = [];
    private readonly Dictionary<object, IAsyncDisposable> _byKey = [];
    private readonly List<Exception> _disposalErrors = [];
    private System.Threading.Tasks.Task? _end;

    // Set by Begin: the scope that was current in the flow that began this one, restored by DisposeAsync.
    private bool _began;
    private AgentRunScope? _previous;

    private AgentRunScope(string? agentId, string? agentRole, AgentRunScope? parent, string? root = null)
    {
        Root = root;
        RunId = Guid.NewGuid().ToString("N");
        AgentId = agentId;
        AgentRole = agentRole;
        Parent = parent;
    }

    /// <summary>The scope of the agent run the calling flow belongs to, or <c>null</c> outside any run.</summary>
    public static AgentRunScope? Current => s_current.Value;

    /// <summary>A unique identifier of this run.</summary>
    public string RunId { get; }

    /// <summary>The identifier of the agent that runs, for diagnostics; <c>null</c> when unknown.</summary>
    public string? AgentId { get; }

    /// <summary>The role or name of the agent that runs, for diagnostics; <c>null</c> when unknown.</summary>
    public string? AgentRole { get; }

    /// <summary>The scope that was current when this one was created (the caller of a delegated run), or <c>null</c>.</summary>
    public AgentRunScope? Parent { get; }

    /// <summary>The virtual working root the run's file tools stay inside, or <c>null</c> when unrestricted.</summary>
    public string? Root { get; }

    /// <summary>Whether <see cref="EndAsync"/> has been called.</summary>
    public bool IsEnded
    {
        get
        {
            lock (_sync)
                return _end is not null;
        }
    }

    /// <summary>The exceptions the resources threw while being released; empty until the end completes.</summary>
    public IReadOnlyList<Exception> DisposalErrors
    {
        get
        {
            lock (_sync)
                return [.. _disposalErrors];
        }
    }

    /// <summary>
    /// Creates a scope without making it current: its owner enters it (<see cref="Enter"/>) around
    /// each piece of work of the run and ends it (<see cref="EndAsync"/>) when the run stops.
    /// </summary>
    /// <param name="agentId">The agent's identifier, for diagnostics.</param>
    /// <param name="agentRole">The agent's role or name, for diagnostics.</param>
    public static AgentRunScope Create(string? agentId = null, string? agentRole = null)
        => new(agentId, agentRole, s_current.Value);

    /// <summary>Creates a scope with a virtual working root without making it current (see <see cref="Create(string?, string?)"/>).</summary>
    /// <param name="agentId">The agent's identifier, for diagnostics.</param>
    /// <param name="agentRole">The agent's role or name, for diagnostics.</param>
    /// <param name="root">The virtual working root, or <c>null</c>.</param>
    public static AgentRunScope Create(string? agentId, string? agentRole, string? root)
        => new(agentId, agentRole, s_current.Value, root);

    /// <summary>
    /// Creates a scope and makes it current for the calling flow. Disposing it restores the scope
    /// that was current before, then ends it: use it with <c>await using</c> at a run's entry point.
    /// </summary>
    /// <param name="agentId">The agent's identifier, for diagnostics.</param>
    /// <param name="agentRole">The agent's role or name, for diagnostics.</param>
    public static AgentRunScope Begin(string? agentId = null, string? agentRole = null)
        => Begin(agentId, agentRole, root: null);

    /// <summary>Creates a scope with a virtual working root and makes it current.</summary>
    /// <param name="agentId">The agent's identifier, for diagnostics.</param>
    /// <param name="agentRole">The agent's role or name, for diagnostics.</param>
    /// <param name="root">The virtual working root, or <c>null</c>.</param>
    public static AgentRunScope Begin(string? agentId, string? agentRole, string? root)
    {
        var previous = s_current.Value;
        var scope = new AgentRunScope(agentId, agentRole, previous, root)
        {
            _began = true,
            _previous = previous,
        };
        s_current.Value = scope;
        return scope;
    }

    /// <summary>
    /// Makes this scope current for the calling flow until the returned handle is disposed, which
    /// restores the scope that was current before.
    /// </summary>
    public IDisposable Enter()
    {
        var previous = s_current.Value;
        s_current.Value = this;
        return new Restore(previous);
    }

    /// <summary>Registers a resource the end of the run releases.</summary>
    /// <param name="resource">The resource.</param>
    /// <exception cref="ObjectDisposedException">The run has ended.</exception>
    public void Register(IAsyncDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        lock (_sync)
        {
            ThrowIfEnded();
            _resources.Add(resource);
        }
    }

    /// <summary>
    /// Returns the resource registered under <paramref name="key"/>, or creates it with
    /// <paramref name="factory"/> and registers it. Concurrent callers get the same instance: the
    /// factory runs under the scope's lock, so it must be cheap (defer expensive work to the
    /// resource's first use).
    /// </summary>
    /// <typeparam name="T">The resource's type.</typeparam>
    /// <param name="key">The key the resource is found by.</param>
    /// <param name="factory">Creates the resource when none is registered under the key.</param>
    /// <exception cref="ObjectDisposedException">The run has ended.</exception>
    /// <exception cref="InvalidOperationException">The key holds a resource of another type.</exception>
    public T GetOrAdd<T>(object key, Func<T> factory) where T : class, IAsyncDisposable
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);
        lock (_sync)
        {
            ThrowIfEnded();
            if (_byKey.TryGetValue(key, out var existing))
                return Cast<T>(key, existing);

            var created = factory() ?? throw new InvalidOperationException(
                $"The factory of the agent run resource '{key}' returned null.");
            _byKey.Add(key, created);
            _resources.Add(created);
            return created;
        }
    }

    /// <summary>Looks up the resource registered under <paramref name="key"/>.</summary>
    /// <typeparam name="T">The resource's type.</typeparam>
    /// <param name="key">The key the resource was registered under.</param>
    /// <param name="resource">The resource, when one of type <typeparamref name="T"/> is registered.</param>
    public bool TryGet<T>(object key, [NotNullWhen(true)] out T? resource) where T : class, IAsyncDisposable
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_sync)
        {
            resource = _byKey.TryGetValue(key, out var existing) ? existing as T : null;
            return resource is not null;
        }
    }

    /// <summary>
    /// Ends the run: releases every resource in the reverse order of registration and never throws.
    /// Idempotent: a second call returns the first end.
    /// </summary>
    public ValueTask EndAsync()
    {
        IAsyncDisposable[] resources;
        lock (_sync)
        {
            if (_end is not null)
                return new ValueTask(_end);
            resources = [.. _resources];
            _resources.Clear();
            _byKey.Clear();
            _end = ReleaseAsync(resources);
        }
        return new ValueTask(_end);
    }

    /// <summary>
    /// Restores the scope that was current before <see cref="Begin(string?, string?, string?)"/> (synchronously, so the
    /// calling flow sees it), then ends the run (<see cref="EndAsync"/>).
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (_began && ReferenceEquals(s_current.Value, this))
            s_current.Value = _previous;
        return EndAsync();
    }

    [SuppressMessage("Design", "CA1031", Justification = "Ending a run never throws: a resource whose disposal fails is recorded and the others are still released.")]
    private async System.Threading.Tasks.Task ReleaseAsync(IAsyncDisposable[] resources)
    {
        for (var i = resources.Length - 1; i >= 0; i--)
        {
            try
            {
                await resources[i].DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lock (_sync)
                    _disposalErrors.Add(ex);
            }
        }
    }

    private void ThrowIfEnded()
    {
        if (_end is not null)
            throw new ObjectDisposedException(nameof(AgentRunScope), $"The agent run '{RunId}' has ended.");
    }

    private static T Cast<T>(object key, IAsyncDisposable existing) where T : class, IAsyncDisposable
        => existing as T ?? throw new InvalidOperationException(
            $"The agent run resource '{key}' is a {existing.GetType().Name}, not a {typeof(T).Name}.");

    private sealed class Restore(AgentRunScope? previous) : IDisposable
    {
        public void Dispose() => s_current.Value = previous;
    }
}
