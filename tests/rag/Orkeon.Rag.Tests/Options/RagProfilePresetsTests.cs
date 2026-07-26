using Orkeon.Domain.Constants.Rag;
using Orkeon.Rag.Abstractions.Options;

namespace Orkeon.Rag.Tests.Options;

/// <summary>
/// Tests for <see cref="RagProfilePresets"/> (RAG-04/C4): each profile expands
/// to the documented composition; unknown names fail loudly with the list.
/// </summary>
public class RagProfilePresetsTests
{
    [Fact]
    public void Fast_IsVectorOnly_NoRerank_DirectTopN()
    {
        var options = RagProfilePresets.Create(RagProfile.Fast);

        Assert.Equal("fast", options.Profile);
        Assert.False(options.Retrieval.Hybrid.Enabled);
        Assert.Equal(RagDefaults.TopK, options.Retrieval.TopK);
        Assert.Equal(RagDefaults.TopK, options.Retrieval.CandidateK); // direct: no wide stage
        Assert.False(options.Rerank.Enabled);
        Assert.Equal("none", options.Rerank.Kind);
        Assert.False(options.Groundedness.Enabled);
        Assert.Equal("none", options.QueryTransform.Mode);
        Assert.Equal(RagDefaults.ContextOrderingEdges, options.Context.Ordering);
    }

    [Fact]
    public void Balanced_IsHybridRrf_WithOnnxRerank_Cascade50To5()
    {
        var options = RagProfilePresets.Create(RagProfile.Balanced);

        Assert.Equal("balanced", options.Profile);
        Assert.True(options.Retrieval.Hybrid.Enabled);
        Assert.Equal(RagDefaults.RrfK, options.Retrieval.Hybrid.RrfK);
        Assert.Equal(RagDefaults.CandidateK, options.Retrieval.CandidateK); // 50
        Assert.True(options.Rerank.Enabled);
        Assert.Equal("onnx", options.Rerank.Kind);
        Assert.Equal(RagDefaults.RerankTopN, options.Rerank.TopN); // 5
        Assert.False(options.Groundedness.Enabled);
    }

    [Fact]
    public void Quality_IsBalanced_WithWiderPool_AndGroundedness()
    {
        var options = RagProfilePresets.Create(RagProfile.Quality);

        Assert.Equal("quality", options.Profile);
        Assert.True(options.Retrieval.Hybrid.Enabled);
        Assert.Equal(RagDefaults.QualityCandidateK, options.Retrieval.CandidateK); // 100
        Assert.True(options.Rerank.Enabled);
        Assert.Equal("onnx", options.Rerank.Kind);
        Assert.True(options.Groundedness.Enabled);
        Assert.Equal("none", options.QueryTransform.Mode); // multi-query lands with RAG-05
    }

    [Fact]
    public void Create_ReturnsAFreshInstanceEachCall()
    {
        var first = RagProfilePresets.Create(RagProfile.Balanced);
        var second = RagProfilePresets.Create(RagProfile.Balanced);

        Assert.NotSame(first, second);
        first.Rerank.TopN = 42;
        Assert.Equal(RagDefaults.RerankTopN, second.Rerank.TopN);
    }

    [Theory]
    [InlineData("fast", RagProfile.Fast)]
    [InlineData("  Balanced ", RagProfile.Balanced)]
    [InlineData("QUALITY", RagProfile.Quality)]
    public void Parse_IsTrimmedAndCaseInsensitive(string name, RagProfile expected)
    {
        Assert.Equal(expected, RagProfilePresets.Parse(name));
    }

    [Fact]
    public void Parse_UnknownName_FailsLoudly_ListingKnownProfiles()
    {
        var ex = Assert.Throws<ArgumentException>(() => RagProfilePresets.Parse("turbo"));

        Assert.Contains("turbo", ex.Message, StringComparison.Ordinal);
        Assert.Contains("fast", ex.Message, StringComparison.Ordinal);
        Assert.Contains("balanced", ex.Message, StringComparison.Ordinal);
        Assert.Contains("quality", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultProfile_IsFast_UntilOnnxShipsInTheBox()
    {
        // Documented deviation from plan §5.2 (balanced as default): balanced
        // requires the opt-in ONNX package, so the dependency-free default is fast.
        Assert.Equal("fast", RagDefaults.DefaultProfile);
        Assert.True(RagProfilePresets.TryParse(RagDefaults.DefaultProfile, out var profile));
        Assert.Equal(RagProfile.Fast, profile);
    }
}
