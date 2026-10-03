using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Evaluation;
using Orkeon.Infrastructure.Evaluation.Evaluators;
using Orkeon.Infrastructure.Evaluation.LlmJudge;
using Orkeon.Application.Configuration;

namespace Orkeon.Infrastructure.Evaluation;

/// <summary>
/// Extension methods for registering evaluation services in the DI container.
/// </summary>
public static class EvaluationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the evaluation framework: the deterministic evaluators and the default
    /// <see cref="IEvaluationSuite"/>, which also carries the LLM-as-Judge evaluators
    /// (coherence, fluency, groundedness) when <see cref="EvaluationOptions.EnableLlmJudge"/>
    /// is set and an <see cref="IChatClient"/> is registered.
    /// </summary>
    /// <remarks>
    /// Idempotent: <c>AddOrkeonInfrastructure()</c> already calls it, and a second call only
    /// adds the configuration binding — no evaluator is registered twice. The
    /// <c>Evaluation</c> section is bound when <paramref name="configuration"/> is given, which
    /// <c>AddOrkeonInfrastructure(configuration)</c> does. The benchmark runner is a dormant
    /// subsystem and is NOT registered here; enable it explicitly with
    /// <see cref="AddOrkeonBenchmarking"/> (R4.9 — see <c>docs/reference/opt-in-subsystems.md</c>).
    /// </remarks>
    public static IServiceCollection AddOrkeonEvaluation(
        this IServiceCollection services, IConfiguration? configuration = null)
    {
        var options = services.AddOptions<EvaluationOptions>();
        if (configuration != null)
            options.Bind(configuration.GetSection(SectionName)).DeclareSettings(SectionName);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEvaluator, FormatComplianceEvaluator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEvaluator, SchemaComplianceEvaluator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEvaluator, TextQualityEvaluator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEvaluator, SimilarityEvaluator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEvaluator, ToolAccuracyEvaluator>());

        // Default suite: every registered evaluator, plus the LLM judges when they are enabled.
        services.TryAddSingleton<IEvaluationSuite>(sp =>
            new EvaluationSuite("Default", sp.GetServices<IEvaluator>().Concat(LlmJudges(sp))));

        return services;
    }

    /// <summary>The configuration section <see cref="AddOrkeonEvaluation"/> binds.</summary>
    private const string SectionName = "Evaluation";

    private static IEnumerable<IEvaluator> LlmJudges(IServiceProvider sp)
    {
        if (!sp.GetRequiredService<IOptions<EvaluationOptions>>().Value.EnableLlmJudge)
            return [];

        var chatClient = sp.GetService<IChatClient>()
            ?? throw new InvalidOperationException(
                "Evaluation:EnableLlmJudge is true but no IChatClient is registered: register one, " +
                "or turn the LLM judges off.");

        return [new CoherenceEvaluator(chatClient), new FluencyEvaluator(chatClient), new GroundednessEvaluator(chatClient)];
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
