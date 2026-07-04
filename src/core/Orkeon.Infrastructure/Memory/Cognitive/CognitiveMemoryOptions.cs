using Orkeon.Domain.Memory;

namespace Orkeon.Infrastructure.Memory.Cognitive;

/// <summary>
/// Configuration options for the cognitive memory system.
/// </summary>
public sealed class CognitiveMemoryOptions
{
    /// <summary>Whether to enable LLM-based content analysis on remember. Default: true.</summary>
    public bool EnableLlmAnalysis { get; set; } = true;

    /// <summary>Whether to enable contradiction detection on remember. Default: true.</summary>
    public bool EnableContradictionDetection { get; set; } = true;

    /// <summary>Number of existing memories to check for contradictions. Default: 10.</summary>
    public int ContradictionCandidateCount { get; set; } = 10;

    /// <summary>Optional model override for analysis calls. Null uses the default provider model.</summary>
    public string? AnalysisModel { get; set; }

    /// <summary>Temperature for LLM analysis calls. Default: 0.1.</summary>
    public float AnalysisTemperature { get; set; } = 0.1f;

    /// <summary>Importance threshold below which memories are candidates for pruning. Default: 0.1.</summary>
    public float PruningThreshold { get; set; } = 0.1f;

    /// <summary>Minimum age in days before a memory can be pruned. Default: 30.</summary>
    public int PruningMinAgeDays { get; set; } = 30;

    /// <summary>Half-life for recency decay in hours (exp(-ln2/halfLife * ageHours)). Default: 69.0.</summary>
    public double RecencyHalfLifeHours { get; set; } = 69.0;

    /// <summary>Default recall options used when none are specified. Default: standard weights.</summary>
    public RecallOptions DefaultRecallOptions { get; set; } = new();
}
