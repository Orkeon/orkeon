using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Evaluation;
using Orkeon.Infrastructure.Evaluation.Evaluators;
using Orkeon.Infrastructure.Evaluation.LlmJudge;

namespace Orkeon.Infrastructure.Evaluation;

/// <summary>
/// Extension methods for registering evaluation services in the DI container.
/// </summary>
public static class EvaluationServiceCollectionExtensions
{
    /// <summary>
    /// Registers evaluation framework services including deterministic evaluators
    /// and optionally LLM-as-Judge evaluators.
    /// </summary>
    /// <remarks>
    /// The benchmark runner is a dormant subsystem and is NOT registered here; enable
    /// it explicitly with <see cref="AddOrkeonBenchmarking"/>
    /// (R4.9 — see <c>docs/reference/opt-in-subsystems.md</c>).
    /// </remarks>
    public static IServiceCollection AddOrkeonEvaluation(
        this IServiceCollection services, IConfiguration? configuration = null)
    {
        // Bind options
        if (configuration != null)
        {
            services.AddOptions<EvaluationOptions>()
                .Bind(configuration.GetSection("Evaluation"));
        }
        else
        {
            services.TryAddSingleton(Options.Create(new EvaluationOptions()));
        }

        // Register deterministic evaluators
        services.AddSingleton<IEvaluator, FormatComplianceEvaluator>();
        services.AddSingleton<IEvaluator, SchemaComplianceEvaluator>();
        services.AddSingleton<IEvaluator, TextQualityEvaluator>();
        services.AddSingleton<IEvaluator, SimilarityEvaluator>();
        services.AddSingleton<IEvaluator, ToolAccuracyEvaluator>();

        // Conditionally register LLM-as-Judge evaluators
        services.AddSingleton<IEvaluator>(sp =>
        {
            var options = sp.GetService<IOptions<EvaluationOptions>>()?.Value ?? new EvaluationOptions();
            if (!options.EnableLlmJudge) return new NoOpEvaluator("Coherence");
            var chatClient = sp.GetService<IChatClient>();
            if (chatClient == null) return new NoOpEvaluator("Coherence");
            return new CoherenceEvaluator(chatClient);
        });

        services.AddSingleton<IEvaluator>(sp =>
        {
            var options = sp.GetService<IOptions<EvaluationOptions>>()?.Value ?? new EvaluationOptions();
            if (!options.EnableLlmJudge) return new NoOpEvaluator("Fluency");
            var chatClient = sp.GetService<IChatClient>();
            if (chatClient == null) return new NoOpEvaluator("Fluency");
            return new FluencyEvaluator(chatClient);
        });

        services.AddSingleton<IEvaluator>(sp =>
        {
            var options = sp.GetService<IOptions<EvaluationOptions>>()?.Value ?? new EvaluationOptions();
            if (!options.EnableLlmJudge) return new NoOpEvaluator("Groundedness");
            var chatClient = sp.GetService<IChatClient>();
            if (chatClient == null) return new NoOpEvaluator("Groundedness");
            return new GroundednessEvaluator(chatClient);
        });

        // Register evaluation suite (default: includes all registered evaluators)
        services.TryAddSingleton<IEvaluationSuite>(sp =>
        {
            var evaluators = sp.GetServices<IEvaluator>()
                .Where(e => e is not NoOpEvaluator)
                .ToList();
            return new EvaluationSuite("Default", evaluators);
        });

        return services;
    }

    /// <summary>
    /// Opt-in: adds the benchmark runner (<see cref="IBenchmarkRunner"/>), which runs an
    /// evaluation suite multiple times per case and computes mean/stddev statistics.
    /// Not registered by <c>AddOrkeonInfrastructure()</c> — call this explicitly.
    /// </summary>
    /// <remarks>
    /// Self-contained: <see cref="BenchmarkRunner"/> has no constructor dependencies
    /// (the evaluation suite is supplied through <c>BenchmarkConfig</c> at call time).
    /// Pair it with <see cref="AddOrkeonEvaluation"/> to build suites from DI.
    /// </remarks>
    public static IServiceCollection AddOrkeonBenchmarking(this IServiceCollection services)
    {
        services.TryAddSingleton<IBenchmarkRunner, BenchmarkRunner>();
        return services;
    }
}

/// <summary>
/// Placeholder evaluator for disabled LLM-as-Judge evaluators.
/// Filtered out when building the evaluation suite.
/// </summary>
internal sealed class NoOpEvaluator : IEvaluator
{
    public string Name { get; }
    public string Description => "Disabled evaluator (LLM judge not enabled).";
    public bool RequiresLlm => true;

    public NoOpEvaluator(string name)
    {
        Name = name;
    }

    public Task<EvaluationScore> EvaluateAsync(EvaluationInput input, CancellationToken ct = default)
    {
        return Task.FromResult(new EvaluationScore(Name, 0.0, "Evaluator disabled."));
    }
}
