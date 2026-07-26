using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orkeon.Domain.Memory;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Retrieval;

namespace Orkeon.Rag.DependencyInjection;

/// <summary>
/// Registration of hybrid retrieval (RAG-04/C2, per-profile since C4): the
/// registered <see cref="IDocumentStore"/> — whichever it is, including a
/// host-registered one — is <b>always</b> wrapped in a
/// <see cref="HybridSearchDocumentStore"/> so ingestion feeds the in-process BM25
/// index; whether a search actually fuses is decided per query
/// (<c>RetrievalQuery.Hybrid</c>, set by the profile presets) with
/// <c>Orkeon:Rag:Retrieval:Hybrid:Enabled</c> as the default mode. A disabled
/// default is a strict behavioural passthrough (identical results to the
/// undecorated store). Called by <c>AddOrkeonRag</c>.
/// </summary>
public static class HybridRetrievalExtensions
{
    /// <summary>Configuration section bound to <see cref="HybridRetrievalOptions"/>.</summary>
    public const string HybridSectionKey = "Orkeon:Rag:Retrieval:Hybrid";

    /// <summary>
    /// Binds <see cref="HybridRetrievalOptions"/> and decorates the last registered
    /// <see cref="IDocumentStore"/> descriptor with the hybrid decorator.
    /// </summary>
    /// <param name="services">The service collection (an <see cref="IDocumentStore"/> must already be registered).</param>
    /// <param name="configuration">Configuration root; both the section form
    /// (<c>Orkeon:Rag:Retrieval:Hybrid:Enabled</c>) and the flat shorthand
    /// (<c>Orkeon:Rag:Retrieval:Hybrid = true</c>) are accepted for the default mode.</param>
    /// <param name="providerResolver">
    /// Optional resolver of the memory provider backing the document store, used by the
    /// decorator to discover native hybrid search. <c>AddOrkeonRag</c> passes its own
    /// store-provider resolution; note that when <c>Orkeon:Rag:Provider</c> selects a
    /// dedicated provider, the resolver yields an equivalently configured instance (same
    /// remote containers) rather than the store's own instance. <see langword="null"/>
    /// falls back to the ambient <see cref="IMemoryProvider"/> when present.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// No <see cref="IDocumentStore"/> is registered.
    /// </exception>
    public static IServiceCollection AddOrkeonHybridRetrieval(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<IServiceProvider, IMemoryProvider>? providerResolver = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(HybridSectionKey);

        services.AddOptions();
        services.Configure<HybridRetrievalOptions>(section);

        // Flat shorthand: `Orkeon:Rag:Retrieval:Hybrid = true` (the section itself
        // carries a scalar value instead of children).
        if (bool.TryParse(section.Value, out var flat))
            services.Configure<HybridRetrievalOptions>(options => options.Enabled = flat);

        // Idempotence: decorating twice would nest two BM25 indexes.
        if (services.Any(descriptor => descriptor.ServiceType == typeof(HybridDecorationMarker)))
            return services;
        services.AddSingleton(HybridDecorationMarker.Instance);

        var storeIndex = LastDocumentStoreIndex(services)
            ?? throw new InvalidOperationException(
                $"AddOrkeonHybridRetrieval requires a registered {nameof(IDocumentStore)}. " +
                "Call it after the document store registration (AddOrkeonRag does).");

        var original = services[storeIndex];
        services[storeIndex] = ServiceDescriptor.Describe(
            typeof(IDocumentStore),
            serviceProvider => new HybridSearchDocumentStore(
                (IDocumentStore)CreateInner(serviceProvider, original),
                serviceProvider.GetRequiredService<IOptions<HybridRetrievalOptions>>().Value,
                ResolveProviderSafely(serviceProvider, providerResolver)),
            original.Lifetime);

        return services;
    }

    private static int? LastDocumentStoreIndex(IServiceCollection services)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(IDocumentStore))
                return i;
        }

        return null;
    }

    private static object CreateInner(IServiceProvider serviceProvider, ServiceDescriptor original)
    {
        if (original.ImplementationInstance is { } instance)
            return instance;

        if (original.ImplementationFactory is { } factory)
            return factory(serviceProvider);

        return ActivatorUtilities.CreateInstance(serviceProvider, original.ImplementationType!);
    }

    /// <summary>Marker registration preventing double decoration.</summary>
    internal sealed class HybridDecorationMarker
    {
        /// <summary>Shared instance (the registration is a pure marker).</summary>
        public static HybridDecorationMarker Instance { get; } = new();
    }

    /// <summary>
    /// Resolves the provider used for native-hybrid discovery. Absence is tolerated (a
    /// host may register a document store with no <see cref="IMemoryProvider"/> at all —
    /// the emulated BM25 + RRF path needs none); genuine misconfigurations (e.g. an
    /// unknown <c>Orkeon:Rag:Provider</c> alias) already failed loudly while creating the
    /// inner store.
    /// </summary>
    private static IMemoryProvider? ResolveProviderSafely(
        IServiceProvider serviceProvider,
        Func<IServiceProvider, IMemoryProvider>? providerResolver)
    {
        if (providerResolver is null)
            return serviceProvider.GetService<IMemoryProvider>();

        try
        {
            return providerResolver(serviceProvider);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
