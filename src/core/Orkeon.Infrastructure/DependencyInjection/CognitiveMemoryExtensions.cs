using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Infrastructure.Memory.Cognitive;

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
            .Bind(configuration.GetSection("Orkeon:CognitiveMemory"));

        services.TryAddSingleton<MemoryAnalyzer>();
        services.TryAddSingleton<ContradictionDetector>();
        services.TryAddSingleton<MemoryConsolidator>();
        services.TryAddSingleton<CompositeScorer>();
        services.TryAddSingleton<CognitiveAnalysisServices>(sp =>
            new CognitiveAnalysisServices(
                sp.GetRequiredService<MemoryAnalyzer>(),
                sp.GetRequiredService<ContradictionDetector>(),
                sp.GetRequiredService<MemoryConsolidator>(),
                sp.GetRequiredService<CompositeScorer>()));
        services.TryAddScoped<ICognitiveMemoryService, CognitiveMemoryService>();

        return services;
    }
}
