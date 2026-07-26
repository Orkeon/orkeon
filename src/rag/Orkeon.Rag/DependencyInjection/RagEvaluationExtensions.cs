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
/// loader, evaluator, report writer, and the one-call harness. Called by
/// <c>AddOrkeonRag</c> (which registers the preset-aware
/// <see cref="IRagProfileResolver"/> itself); every registration is
/// <c>TryAdd*</c> so a host-provided implementation always wins.
/// </summary>
public static class RagEvaluationExtensions
{
    /// <summary>Registers the evaluation harness services.</summary>
    public static IServiceCollection AddOrkeonRagEvaluation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

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
