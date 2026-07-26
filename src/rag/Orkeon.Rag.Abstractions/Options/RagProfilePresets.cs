using Orkeon.Domain.Constants.Rag;

namespace Orkeon.Rag.Abstractions.Options;

/// <summary>
/// Expands a <see cref="RagProfile"/> into a complete <see cref="RagOptions"/>
/// (plan §5.2). The preset is a starting point: configuration bound over the
/// returned instance overrides any value individually.
/// </summary>
public static class RagProfilePresets
{
    /// <summary>Canonical name of <see cref="RagProfile.Fast"/>.</summary>
    public const string FastName = "fast";

    /// <summary>Canonical name of <see cref="RagProfile.Balanced"/>.</summary>
    public const string BalancedName = "balanced";

    /// <summary>Canonical name of <see cref="RagProfile.Quality"/>.</summary>
    public const string QualityName = "quality";

    /// <summary>Canonical name of <see cref="RagProfile.Adaptive"/>.</summary>
    public const string AdaptiveName = "adaptive";

    /// <summary>Canonical name of <see cref="RagProfile.Corrective"/>.</summary>
    public const string CorrectiveName = "corrective";

    /// <summary>The known profile names, in preset order.</summary>
    public static IReadOnlyList<string> KnownProfileNames { get; } =
        [FastName, BalancedName, QualityName, AdaptiveName, CorrectiveName];

    /// <summary>
    /// Parses a profile name (trimmed, case-insensitive) into its
    /// <see cref="RagProfile"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The name matches no known profile — the message lists every known name
    /// (never a silent fallback).
    /// </exception>
    public static RagProfile Parse(string profileName)
    {
        if (TryParse(profileName, out var profile))
            return profile;

        throw new ArgumentException(
            $"Unknown RAG profile '{profileName?.Trim()}'. Known profiles: " +
            $"{string.Join(", ", KnownProfileNames)}.",
            nameof(profileName));
    }

    /// <summary>Attempts to parse a profile name; returns <c>false</c> when unknown.</summary>
    public static bool TryParse(string? profileName, out RagProfile profile)
    {
        switch (profileName?.Trim().ToUpperInvariant())
        {
            case "FAST":
                profile = RagProfile.Fast;
                return true;
            case "BALANCED":
                profile = RagProfile.Balanced;
                return true;
            case "QUALITY":
                profile = RagProfile.Quality;
                return true;
            case "ADAPTIVE":
                profile = RagProfile.Adaptive;
                return true;
            case "CORRECTIVE":
                profile = RagProfile.Corrective;
                return true;
            default:
                profile = default;
                return false;
        }
    }

    /// <summary>Canonical (lowercase) name of <paramref name="profile"/>.</summary>
    public static string NameOf(RagProfile profile) => profile switch
    {
        RagProfile.Fast => FastName,
        RagProfile.Balanced => BalancedName,
        RagProfile.Quality => QualityName,
        RagProfile.Adaptive => AdaptiveName,
        RagProfile.Corrective => CorrectiveName,
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown RAG profile."),
    };

    /// <summary>
    /// Creates a fresh, fully populated <see cref="RagOptions"/> for
    /// <paramref name="profile"/>. Each call returns a new instance (presets are
    /// mutated by configuration binding).
    /// </summary>
    public static RagOptions Create(RagProfile profile) => profile switch
    {
        // Fast — vector only, no rerank, TopN retrieved directly.
        RagProfile.Fast => new RagOptions
        {
            Profile = FastName,
            Retrieval = new RagRetrievalOptions
            {
                TopK = RagDefaults.TopK,
                CandidateK = RagDefaults.TopK, // direct: no wide stage without a reranker
                Hybrid = new RagHybridOptions { Enabled = false },
            },
            Rerank = new RagRerankOptions { Enabled = false, Kind = RagDefaults.RerankNone },
            Groundedness = new RagGroundednessOptions { Enabled = false },
        },

        // Balanced — hybrid BM25 + RRF, ONNX cross-encoder 50 → 5.
        RagProfile.Balanced => new RagOptions
        {
            Profile = BalancedName,
            Retrieval = new RagRetrievalOptions
            {
                TopK = RagDefaults.TopK,
                CandidateK = RagDefaults.CandidateK,
                Hybrid = new RagHybridOptions { Enabled = true, RrfK = RagDefaults.RrfK },
            },
            Rerank = new RagRerankOptions
            {
                Enabled = true,
                Kind = RagDefaults.RerankOnnx,
                TopN = RagDefaults.RerankTopN,
            },
            Groundedness = new RagGroundednessOptions { Enabled = false },
        },

        // Quality — Balanced + wider candidate pool + groundedness verification.
        // Query transform stays "none" by default: the RAG-05 transformers are
        // LLM-backed and remain a per-key opt-in on every profile.
        RagProfile.Quality => new RagOptions
        {
            Profile = QualityName,
            Retrieval = new RagRetrievalOptions
            {
                TopK = RagDefaults.TopK,
                CandidateK = RagDefaults.QualityCandidateK,
                Hybrid = new RagHybridOptions { Enabled = true, RrfK = RagDefaults.RrfK },
            },
            Rerank = new RagRerankOptions
            {
                Enabled = true,
                Kind = RagDefaults.RerankOnnx,
                TopN = RagDefaults.RerankTopN,
            },
            Groundedness = new RagGroundednessOptions { Enabled = true },
        },

        // Adaptive — routing profile (RAG-05/C3): the pipeline is composed by the
        // profile resolver (classifier + delegation to balanced/quality), not built
        // from a RagOptions preset. The options returned here are those of its
        // SingleShot delegate (balanced) with the canonical adaptive name, so that
        // configuration binding (Orkeon:Rag:Profile = adaptive) keeps working on
        // hosts resolving RagOptions directly.
        RagProfile.Adaptive => CreateAdaptiveDelegateOptions(),

        // Corrective — CRAG graph profile (RAG-06): the pipeline is the corrective
        // StateGraph composed by the profile resolver (CorrectiveRagPipeline), not
        // a staged pipeline expanded from these options. The preset feeds the
        // graph's nodes: hybrid BM25+RRF retrieval (rewritten probes need the
        // lexical leg to bridge vocabulary gaps), NO linear rerank stage (the
        // graph corrects through evaluate → rewrite loops instead of reranking —
        // and therefore needs no opt-in ONNX package), and Groundedness.Enabled
        // stays false ON PURPOSE: the graph runs its native check_groundedness
        // node whenever an IGroundednessChecker is registered, so the staged
        // pipeline's flag would be dead configuration here — and a double check
        // if these options ever fed a linear pipeline.
        RagProfile.Corrective => new RagOptions
        {
            Profile = CorrectiveName,
            Retrieval = new RagRetrievalOptions
            {
                TopK = RagDefaults.TopK,
                CandidateK = RagDefaults.CandidateK,
                Hybrid = new RagHybridOptions { Enabled = true, RrfK = RagDefaults.RrfK },
            },
            Rerank = new RagRerankOptions { Enabled = false, Kind = RagDefaults.RerankNone },
            Groundedness = new RagGroundednessOptions { Enabled = false },
            Corrective = new RagCorrectiveOptions(),
        },

        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown RAG profile."),
    };

    private static RagOptions CreateAdaptiveDelegateOptions()
    {
        var options = Create(RagProfile.Balanced);
        options.Profile = AdaptiveName;
        return options;
    }
}
