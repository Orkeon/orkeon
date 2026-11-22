using Microsoft.Extensions.DependencyInjection;

namespace Orkeon.Cli.Commands.Scripting.DependencyInjection;

/// <summary>
/// The one scope the REPL's script engines read their scoped ports from (System One's pick #460, the pruning an act's
/// <c>prune</c> asks for #461): the engine factory is a singleton for the REPL's life, and a scoped service is never
/// resolved from the root. A singleton of the container, so the container disposes the scope with itself.
/// </summary>
internal sealed class ScriptEngineRunScope : IDisposable
{
    private readonly IServiceScope _scope;

    /// <summary>Opens the scope over <paramref name="services"/>, the container's root.</summary>
    public ScriptEngineRunScope(IServiceProvider services) =>
        _scope = (services ?? throw new ArgumentNullException(nameof(services))).CreateScope();

    /// <summary>The scope's services.</summary>
    public IServiceProvider Services => _scope.ServiceProvider;

    /// <inheritdoc />
    public void Dispose() => _scope.Dispose();
}
