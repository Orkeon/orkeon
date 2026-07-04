using Orkeon.Infrastructure.Knowledge.Retrieval;

namespace Orkeon.Infrastructure.Tests.Knowledge.Retrieval;

public class HybridScorerTestsFixture
{
    public static float ComputeRRFScore(int semanticRank, int keywordRank)
        => HybridScorer.ComputeRRFScore(semanticRank, keywordRank);

    public static float ComputeWeightedScore(float semanticScore, float keywordScore, float semanticWeight, float keywordWeight)
        => HybridScorer.ComputeWeightedScore(semanticScore, keywordScore, semanticWeight, keywordWeight);

    public static float ComputeKeywordScore(string query, string document)
        => HybridScorer.ComputeKeywordScore(query, document);
}
