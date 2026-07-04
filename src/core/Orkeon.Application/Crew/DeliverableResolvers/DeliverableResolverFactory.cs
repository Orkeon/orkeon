using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Application.Crew.DeliverableResolvers;

/// <summary>
/// Default implementation of <see cref="IDeliverableResolverFactory"/>. Resolves resolvers
/// lazily from the container so that registrations with missing optional dependencies
/// (e.g. an <see cref="IDeliverableResolver"/> requiring <c>IFileSystemService</c>, itself
/// host-registered) do not break orchestrator construction in minimal-DI contexts.
/// </summary>
public sealed class DeliverableResolverFactory : IDeliverableResolverFactory
{
    private readonly IServiceProvider _sp;

    /// <summary>Initializes a new instance backed by the given service provider.</summary>
    public DeliverableResolverFactory(IServiceProvider sp)
    {
        _sp = sp ?? throw new ArgumentNullException(nameof(sp));
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Optional-dependency fault barrier: resolving IDeliverableResolver services can throw in a minimal host (e.g. no IFileSystemService registered); the factory returns null rather than breaking orchestrator construction.")]
    public IDeliverableResolver? GetFor(DeliverableSource source)
    {
        IEnumerable<IDeliverableResolver> resolvers;
        try
        {
            resolvers = _sp.GetServices<IDeliverableResolver>();
        }
        catch
        {
            // Missing optional dependency (e.g. no IFileSystemService registered in a minimal host).
            return null;
        }

        foreach (var r in resolvers)
        {
            if (r.SupportedSource == source)
                return r;
        }
        return null;
    }
}
