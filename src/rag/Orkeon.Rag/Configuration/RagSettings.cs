using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Configuration;
using Orkeon.Domain.Constants.Rag;
using Orkeon.Rag.Abstractions.Options;
using Orkeon.Rag.Factories;

namespace Orkeon.Rag.Configuration;

/// <summary>
/// The effective <see cref="RagOptions"/> as validated options (GAP-40): the <c>Orkeon:Rag:Profile</c>
/// preset with every <c>Orkeon:Rag</c> key bound over it (<see cref="RagOptionsFactory.Build"/>),
/// judged when a host starts — the profile's name, the reranker, the query transformer and the context
/// ordering it names, against what this host registered, and the numbers that must be positive. They
/// used to be judged at the first RAG query of a run, after the model had been called.
/// </summary>
internal static class RagSettings
{
    /// <summary>
    /// Registers the effective <see cref="RagOptions"/> — as options, and as the singleton the
    /// pipelines take — once, from <paramref name="configuration"/>. A <see cref="RagOptions"/>
    /// singleton the host registered first wins, as it always did.
    /// </summary>
    public static void AddRagOptions(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.IsDeclared(RagOptionsFactory.SectionKey, typeof(RagOptions)))
            return;

        services.AddOptions<RagOptions>()
            .DeclareSettings(RagOptionsFactory.SectionKey)
            .ValidateSettings((options, provider) => Problems(options, provider, configuration));
        services.AddTransient<IOptionsFactory<RagOptions>>(provider => new ProfileOptionsFactory(
            configuration,
            provider.GetServices<IConfigureOptions<RagOptions>>(),
            provider.GetServices<IPostConfigureOptions<RagOptions>>(),
            provider.GetServices<IValidateOptions<RagOptions>>()));
        services.TryAddSingleton(provider => provider.GetRequiredService<IOptions<RagOptions>>().Value);
    }

    /// <summary>
    /// What a host refuses in the effective options: a name its factories do not offer, a context
    /// ordering that is none, a number that must be above zero. A name the profile preset set rather
    /// than the settings is said with its profile — a C# host without the ONNX reranker refuses
    /// <c>balanced</c> and <c>quality</c>, naming <c>onnx</c>.
    /// </summary>
    private static IEnumerable<string> Problems(RagOptions options, IServiceProvider provider, IConfiguration configuration)
    {
        var section = configuration.GetSection(RagOptionsFactory.SectionKey);

        if (Unknown(options.Rerank.Kind, provider.GetService<RerankerFactory>()) is { } rerankers)
        {
            yield return NotOffered(section, "Rerank:Kind", options.Rerank.Kind, options.Profile, "reranker", rerankers)
                + (string.Equals(options.Rerank.Kind.Trim(), "onnx", StringComparison.OrdinalIgnoreCase)
                    ? " The onnx reranker comes with the Orkeon.Rag.Onnx package (AddOrkeonOnnxReranker), which orkeon, orkeon-host and orkeon-repl add."
                    : string.Empty);
        }

        if (!string.Equals(options.QueryTransform.Mode?.Trim(), RagDefaults.QueryTransformNone, StringComparison.OrdinalIgnoreCase)
            && Unknown(options.QueryTransform.Mode, provider.GetService<QueryTransformerFactory>()) is { } transformers)
        {
            yield return NotOffered(section, "QueryTransform:Mode", options.QueryTransform.Mode!, options.Profile, "query transformer", transformers);
        }

        var ordering = options.Context.Ordering?.Trim();
        if (!string.Equals(ordering, RagDefaults.ContextOrderingEdges, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(ordering, RagDefaults.ContextOrderingLinear, StringComparison.OrdinalIgnoreCase))
        {
            yield return $"{section.Path}:Context:Ordering is '{options.Context.Ordering}', which is not a context ordering: write " +
                         $"{RagDefaults.ContextOrderingEdges} or {RagDefaults.ContextOrderingLinear}.";
        }

        foreach (var (key, value) in new (string, int)[]
                 {
                     ("Retrieval:TopK", options.Retrieval.TopK),
                     ("Retrieval:CandidateK", options.Retrieval.CandidateK),
                     ("Retrieval:Hybrid:RrfK", options.Retrieval.Hybrid.RrfK),
                     ("Rerank:TopN", options.Rerank.TopN),
                     ("Context:MaxTokens", options.Context.MaxTokens),
                     ("QueryTransform:VariantCount", options.QueryTransform.VariantCount),
                 })
        {
            if (value <= 0)
                yield return $"{section.Path}:{key} is '{value.ToString(CultureInfo.InvariantCulture)}', which must be above zero.";
        }
    }

    /// <summary>The names <paramref name="factory"/> offers when it does not offer <paramref name="name"/>; null when it does, or when the host registered no such factory.</summary>
    private static IReadOnlyCollection<string>? Unknown<TComponent>(string? name, NamedRagComponentFactory<TComponent>? factory)
        where TComponent : class
    {
        if (factory is null || string.IsNullOrWhiteSpace(name) || factory.IsKnown(name))
            return null;
        return factory.KnownNames;
    }

    private static string NotOffered(
        IConfigurationSection section, string key, string value, string profile, string kind, IReadOnlyCollection<string> known)
    {
        // A name the settings did not write came from the profile's preset: said so, the operator
        // looks for it in Orkeon:Rag:Profile, not in a key the file does not have.
        var origin = section.GetSection(key).Value is null ? $" (set by the {profile} profile)" : string.Empty;
        return $"{section.Path}:{key} is '{value.Trim()}'{origin}, which is not a {kind} this host offers: write one of " +
               $"{string.Join(", ", known)}.";
    }

    /// <summary>
    /// Creates the options from the profile preset and the section instead of the parameterless
    /// constructor; the registered setups, post-setups and rules run on them as on any options. A
    /// profile name that is none is refused like a rule's failure, naming its key.
    /// </summary>
    private sealed class ProfileOptionsFactory(
        IConfiguration configuration,
        IEnumerable<IConfigureOptions<RagOptions>> setups,
        IEnumerable<IPostConfigureOptions<RagOptions>> postConfigures,
        IEnumerable<IValidateOptions<RagOptions>> validations)
        : OptionsFactory<RagOptions>(setups, postConfigures, validations)
    {
        protected override RagOptions CreateInstance(string name)
        {
            var section = configuration.GetSection(RagOptionsFactory.SectionKey);
            var profile = section[nameof(RagOptions.Profile)];
            if (profile is not null && !RagProfilePresets.TryParse(profile, out _))
            {
                throw new OptionsValidationException(name, typeof(RagOptions),
                [
                    $"{section.Path}:{nameof(RagOptions.Profile)} is '{profile.Trim()}', an unknown RAG profile. " +
                    $"Known profiles: {string.Join(", ", RagProfilePresets.KnownProfileNames)}.",
                ]);
            }

            return RagOptionsFactory.Build(configuration);
        }
    }
}
