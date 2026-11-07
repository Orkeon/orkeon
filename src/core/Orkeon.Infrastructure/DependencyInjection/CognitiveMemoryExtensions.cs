using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.Tools;
using Orkeon.Infrastructure.Memory.Cognitive;
using Orkeon.Application.Configuration;

namespace Orkeon.Infrastructure.DependencyInjection;

/// <summary>
/// Extension methods for registering cognitive memory services.
/// </summary>
public static class CognitiveMemoryExtensions
{
    /// <summary>
    /// Registers the cognitive memory system including LLM-powered analysis,
    /// contradiction detection, consolidation, and composite scoring.
    /// Reads configuration from the "Orkeon:CognitiveMemory" section.
    /// </summary>
    public static IServiceCollection AddOrkeonCognitiveMemory(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<CognitiveMemoryOptions>()
            .Bind(configuration.GetSection("Orkeon:CognitiveMemory"))
            .DeclareSettings("Orkeon:CognitiveMemory");

        // The three LLM services run on the profile Orkeon:CognitiveMemory:Profile names, so their
        // calls enter the metered path like any other (FWK-04); never on a bare root provider.
        services.TryAddSingleton(sp => new MemoryAnalyzer(
            AnalysisProvider(sp), sp.GetRequiredService<IOptions<CognitiveMemoryOptions>>(), sp.GetRequiredService<ILogger<MemoryAnalyzer>>()));
        services.TryAddSingleton(sp => new ContradictionDetector(
            AnalysisProvider(sp), sp.GetRequiredService<IOptions<CognitiveMemoryOptions>>(), sp.GetRequiredService<ILogger<ContradictionDetector>>()));
        services.TryAddSingleton(sp => new MemoryConsolidator(
            AnalysisProvider(sp), sp.GetRequiredService<IEmbeddingProvider>(),
            sp.GetRequiredService<IOptions<CognitiveMemoryOptions>>(), sp.GetRequiredService<ILogger<MemoryConsolidator>>()));
        services.TryAddSingleton<CompositeScorer>();
        services.TryAddSingleton<CognitiveAnalysisServices>(sp =>
            new CognitiveAnalysisServices(
                sp.GetRequiredService<MemoryAnalyzer>(),
                sp.GetRequiredService<ContradictionDetector>(),
                sp.GetRequiredService<MemoryConsolidator>(),
                sp.GetRequiredService<CompositeScorer>()));
        // A singleton: every dependency is one, and the crew is an argument of each call. A scoped
        // registration could not be resolved from the root provider the tool and the runners use.
        services.TryAddSingleton<ICognitiveMemoryService, CognitiveMemoryService>();

        // memory_recall: handed to a crew with memory: true by the toolbelt, never to another.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseTool, MemoryRecallTool>());

        return services;
    }

    /// <summary>
    /// The provider of the named profile, the default profile's when none is named — always through
    /// the registry when the host has one; the host's own provider only when it registered no registry.
    /// </summary>
    private static ILlmProvider AnalysisProvider(IServiceProvider sp)
    {
        var profile = sp.GetRequiredService<IOptions<CognitiveMemoryOptions>>().Value.Profile;
        var registry = sp.GetService<ILlmProfileRegistry>();
        if (registry is not null)
            return registry.Resolve(string.IsNullOrWhiteSpace(profile) ? null : profile).Provider;

        return string.IsNullOrWhiteSpace(profile)
            ? sp.GetRequiredService<ILlmProvider>()
            : throw new InvalidOperationException(
                $"Orkeon:CognitiveMemory:Profile names '{profile}', but no LLM profile registry is registered (AddOrkeonLlmProfiles).");
    }
}
