using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.FileSystem;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Evaluation;

namespace Orkeon.Rag.DependencyInjection;

/// <summary>
/// Registration of the RAG evaluation harness (RAG-04/C1, plan §9): dataset
/// loader, evaluator, report writer, profile-resolution hook, and the one-call
/// harness. Called by <c>AddOrkeonRag</c>; every registration is <c>TryAdd*</c>
/// so a host-provided implementation (e.g. a preset-aware
/// <see cref="IRagProfileResolver"/> once profiles land) always wins.
/// </summary>
public static class RagEvaluationExtensions
{
    /// <summary>Registers the evaluation harness services.</summary>
    public static IServiceCollection AddOrkeonRagEvaluation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Profile hook (RAG-04/C4 seam): the documented default maps every profile
        // name to the single registered IRagPipeline.
        services.TryAddSingleton<IRagProfileResolver>(sp =>
            new DefaultRagProfileResolver(sp.GetRequiredService<IRagPipeline>()));

        services.TryAddSingleton<IRagEvalDatasetLoader>(sp =>
            new RagEvalDatasetYamlLoader(sp.GetRequiredService<IFileSystemService>()));

        services.TryAddSingleton(sp =>
            new RagEvalReportWriter(sp.GetRequiredService<IFileSystemService>()));

        // The judge chat client is optional: absent, every run uses (and labels)
        // the deterministic heuristic judge.
        services.TryAddSingleton<IRagEvaluator>(sp => new RagEvaluator(
            sp.GetRequiredService<IRagProfileResolver>(),
            sp.GetService<IChatClient>(),
            sp.GetService<ILogger<RagEvaluator>>()));

        services.TryAddSingleton<IRagEvalHarness>(sp => new RagEvalHarness(
            sp.GetRequiredService<IRagEvalDatasetLoader>(),
            sp.GetRequiredService<IRagEvaluator>(),
            sp.GetRequiredService<IIngestionPipeline>(),
            sp.GetRequiredService<IFileSystemService>(),
            sp.GetRequiredService<RagEvalReportWriter>(),
            sp.GetService<ILogger<RagEvalHarness>>()));

        return services;
    }
}
