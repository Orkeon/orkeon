using Orkeon.Domain.Common;

namespace Orkeon.Domain.Agent.ValueObjects;

/// <summary>Result of agent selection process.</summary>
public record AgentSelectionResult
{
    /// <summary>Confidence score threshold for high-confidence selections.</summary>
    public const double HighConfidenceThreshold = 0.8;

    /// <summary>Confidence score threshold below which selections are considered low confidence.</summary>
    public const double LowConfidenceThreshold = 0.5;

    /// <summary>Gets the identifier of the selected agent, or null if selection failed.</summary>
    public AgentId? SelectedAgentId { get; }
    /// <summary>Gets a value indicating whether the selection was successful.</summary>
    public bool IsSuccess { get; }
    /// <summary>Gets the reason for the selection result.</summary>
    public string? Reason { get; }
    /// <summary>Gets the confidence score of the selection (0.0 to 1.0).</summary>
    public double ConfidenceScore { get; }
    /// <summary>Gets the scores for all considered agents.</summary>
    public IReadOnlyDictionary<AgentId, double> AgentScores { get; }
    /// <summary>Gets the list of agent identifiers that were considered during selection.</summary>
    public IReadOnlyList<AgentId> ConsideredAgents { get; }

    /// <summary>Initializes a new instance of <see cref="AgentSelectionResult"/>.</summary>
    /// <param name="selectedAgentId">The identifier of the selected agent.</param>
    /// <param name="isSuccess">Whether the selection succeeded.</param>
    /// <param name="reason">The reason for this result.</param>
    /// <param name="confidenceScore">The confidence score (0.0 to 1.0).</param>
    /// <param name="agentScores">Scores for considered agents.</param>
    /// <param name="consideredAgents">Agents that were considered.</param>
    private AgentSelectionResult(
        AgentId? selectedAgentId,
        bool isSuccess,
        string? reason = null,
        double confidenceScore = 0.0,
        IDictionary<AgentId, double>? agentScores = null,
        IEnumerable<AgentId>? consideredAgents = null)
    {
        if (isSuccess && selectedAgentId is null)
            throw new ArgumentException("A successful selection must have a SelectedAgentId.");

        if (double.IsNaN(confidenceScore) || double.IsInfinity(confidenceScore))
            confidenceScore = 0.0;

        if (confidenceScore < 0.0 || confidenceScore > 1.0)
            throw new ArgumentOutOfRangeException(nameof(confidenceScore),
                $"ConfidenceScore must be between 0.0 and 1.0, but was {confidenceScore}.");

        SelectedAgentId = selectedAgentId;
        IsSuccess = isSuccess;
        Reason = reason;
        ConfidenceScore = confidenceScore;
        AgentScores = agentScores != null
            ? new Dictionary<AgentId, double>(agentScores)
            : [];
        ConsideredAgents = consideredAgents != null
            ? consideredAgents.ToList().AsReadOnly()
            : Array.Empty<AgentId>();
    }

    /// <summary>
    /// Creates a new <see cref="AgentSelectionResult"/> with all parameters explicitly specified.
    /// Intended for use in test assemblies and advanced scenarios; prefer <see cref="Success"/> or <see cref="Failure"/> for production code.
    /// </summary>
    internal static AgentSelectionResult Create(
        AgentId? selectedAgentId,
        bool isSuccess,
        string? reason = null,
        double confidenceScore = 0.0,
        IDictionary<AgentId, double>? agentScores = null,
        IEnumerable<AgentId>? consideredAgents = null)
    {
        return new AgentSelectionResult(selectedAgentId, isSuccess, reason, confidenceScore, agentScores, consideredAgents);
    }

    /// <summary>Creates a successful selection result for the given agent.</summary>
    /// <param name="agentId">The identifier of the selected agent.</param>
    /// <param name="confidence">The confidence score.</param>
    /// <returns>A successful <see cref="AgentSelectionResult"/>.</returns>
    public static AgentSelectionResult Success(AgentId agentId, double confidence = 1.0)
    {
        return new AgentSelectionResult(agentId, true, confidenceScore: confidence);
    }

    /// <summary>Creates a failed selection result with the given reason.</summary>
    /// <param name="reason">The reason for the failure.</param>
    /// <returns>A failed <see cref="AgentSelectionResult"/>.</returns>
    public static AgentSelectionResult Failure(string reason)
    {
        return new AgentSelectionResult(null, false, reason);
    }

    /// <summary>Gets a value indicating whether this result has a high confidence score (>= 0.8).</summary>
    public bool IsHighConfidence => ConfidenceScore >= HighConfidenceThreshold;
    /// <summary>Gets a value indicating whether this result has a low confidence score (&lt; 0.5).</summary>
    public bool IsLowConfidence => ConfidenceScore < LowConfidenceThreshold;

    /// <summary>Gets the selected agent identifier (backward compatibility alias for <see cref="SelectedAgentId"/>).</summary>
    public AgentId? SelectedAgent => SelectedAgentId;
    /// <summary>Gets the confidence score (backward compatibility alias for <see cref="ConfidenceScore"/>).</summary>
    public double Score => ConfidenceScore;

    /// <inheritdoc />
    public virtual bool Equals(AgentSelectionResult? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return SelectedAgentId == other.SelectedAgentId &&
               IsSuccess == other.IsSuccess &&
               Reason == other.Reason &&
               ConfidenceScore == other.ConfidenceScore &&
               AgentScores.Count == other.AgentScores.Count &&
               AgentScores.All(kvp => other.AgentScores.TryGetValue(kvp.Key, out var value) && value == kvp.Value) &&
               ConsideredAgents.SequenceEqual(other.ConsideredAgents);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SelectedAgentId);
        hash.Add(IsSuccess);
        hash.Add(Reason);
        hash.Add(ConfidenceScore);
        hash.Add(AgentScores.Count);
        hash.Add(ConsideredAgents.Count);
        return hash.ToHashCode();
    }
}
