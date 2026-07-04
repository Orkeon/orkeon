using System.Globalization;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Application.Crew;

/// <summary>
/// Recommendation for crew composition.
/// </summary>
public record CompositionRecommendation(
    string Title,
    string Rationale,
    IReadOnlyList<DomainAgent> RecommendedAgents,
    double ConfidenceScore,
    Dictionary<string, object>? Metadata = null)
{
    /// <summary>
    /// To String.
    /// </summary>
    public override string ToString()
    {
        return $"CompositionRecommendation {{ Title = {Title}, Rationale = {Rationale}, RecommendedAgents = {RecommendedAgents}, ConfidenceScore = {ConfidenceScore.ToString(CultureInfo.InvariantCulture)}, Metadata = {Metadata} }}";
    }
}
