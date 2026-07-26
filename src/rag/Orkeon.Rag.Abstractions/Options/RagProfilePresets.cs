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

    /// <summary>The known profile names, in preset order.</summary>
    public static IReadOnlyList<string> KnownProfileNames { get; } =
        [FastName, BalancedName, QualityName];

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
        // Query transform stays "none" until RAG-05 ships the transformers.
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

        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown RAG profile."),
    };
}
