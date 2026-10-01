using Orkeon.Domain.Knowledge;
using Orkeon.Rag.Augmentation;
using Orkeon.Rag.Tests.Doubles;

namespace Orkeon.Rag.Tests.Augmentation;

/// <summary>
/// GAP-02: an attachment's <c>profile</c> chooses the retrieval pipeline. The attachment's
/// own profile wins, then the default the augmenter was built with (the host's
/// <c>Orkeon:Rag:Profile</c>; the crew's <c>rag.defaults.profile</c> is copied onto the
/// attachments at crew load). Retrieval only: the generating half is never called, and a
/// profile whose pipeline cannot retrieve alone fails instead of silently generating.
/// </summary>
public class KnowledgeContextAugmenterProfileTests
{
    [Fact]
    public async Task Two_attachments_with_different_profiles_each_query_the_pipeline_of_their_profile()
    {
        var fast = new FakeRetrievalPipeline { Citations = [FakeRetrievalPipeline.Cite(1, "f1", "From the fast pipeline.")] };
        var quality = new FakeRetrievalPipeline { Citations = [FakeRetrievalPipeline.Cite(1, "q1", "From the quality pipeline.")] };
        var resolver = new MapProfileResolver();
        resolver.Pipelines["fast"] = fast;
        resolver.Pipelines["quality"] = quality;
        var augmenter = new KnowledgeContextAugmenter(resolver, defaultProfile: "fast");

        var block = await augmenter.BuildContextAsync(
            [
                KnowledgeAttachment.Create("produits", topK: 3, profile: "quality"),
                KnowledgeAttachment.Create("procedures", topK: 4),
            ],
            "how do I get a refund?",
            TestContext.Current.CancellationToken);

        Assert.Equal(["quality", "fast"], resolver.ResolvedProfiles);
        var qualityQuery = Assert.Single(quality.RetrieveCalls);
        Assert.Equal("produits", qualityQuery.Collection);
        Assert.Equal(3, qualityQuery.TopN);
        Assert.Equal("how do I get a refund?", qualityQuery.Text);
        var fastQuery = Assert.Single(fast.RetrieveCalls);
        Assert.Equal("procedures", fastQuery.Collection);
        Assert.Equal(4, fastQuery.TopN);
        Assert.Equal(0, fast.QueryCalls + quality.QueryCalls);

        Assert.NotNull(block);
        Assert.Contains("[1] (collection: produits, source: q1.md, score: 0.90)", block.Text, StringComparison.Ordinal);
        Assert.Contains("From the quality pipeline.", block.Text, StringComparison.Ordinal);
        Assert.Contains("[2] (collection: procedures, source: f1.md, score: 0.90)", block.Text, StringComparison.Ordinal);
        Assert.Equal(["q1", "f1"], block.Citations.Select(c => c.ChunkId));
    }

    [Fact]
    public async Task An_attachment_without_profile_uses_the_default_profile()
    {
        var balanced = new FakeRetrievalPipeline { Citations = [FakeRetrievalPipeline.Cite(1, "b1", "Balanced.")] };
        var resolver = new MapProfileResolver();
        resolver.Pipelines["balanced"] = balanced;
        var augmenter = new KnowledgeContextAugmenter(resolver, defaultProfile: "balanced");

        await augmenter.BuildContextAsync(
            [KnowledgeAttachment.Create("docs")], "query", TestContext.Current.CancellationToken);

        Assert.Equal(["balanced"], resolver.ResolvedProfiles);
        Assert.Single(balanced.RetrieveCalls);
    }

    [Fact]
    public async Task The_full_passage_is_injected_not_the_citation_snippet()
    {
        var passage = new string('p', 300) + " END";
        var pipeline = new FakeRetrievalPipeline
        {
            Citations = [FakeRetrievalPipeline.Cite(1, "c1", passage) with { Snippet = passage[..200] }],
        };
        var resolver = new MapProfileResolver();
        resolver.Pipelines["fast"] = pipeline;
        var augmenter = new KnowledgeContextAugmenter(resolver, defaultProfile: "fast");

        var block = await augmenter.BuildContextAsync(
            [KnowledgeAttachment.Create("docs")], "query", TestContext.Current.CancellationToken);

        Assert.NotNull(block);
        Assert.Contains(passage, block.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_profile_fails_loudly()
    {
        var resolver = new MapProfileResolver();
        resolver.Pipelines["fast"] = new FakeRetrievalPipeline();
        var augmenter = new KnowledgeContextAugmenter(resolver, defaultProfile: "fast");

        await Assert.ThrowsAsync<ArgumentException>(() => augmenter.BuildContextAsync(
            [KnowledgeAttachment.Create("docs", profile: "turbo")], "query", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_profile_whose_pipeline_cannot_retrieve_alone_fails_instead_of_generating()
    {
        var generating = new FakeRagPipeline();
        var resolver = new MapProfileResolver();
        resolver.Pipelines["corrective"] = generating;
        var augmenter = new KnowledgeContextAugmenter(resolver, defaultProfile: "fast");

        var error = await Assert.ThrowsAsync<NotSupportedException>(() => augmenter.BuildContextAsync(
            [KnowledgeAttachment.Create("docs", profile: "corrective")], "query", TestContext.Current.CancellationToken));

        Assert.Contains("corrective", error.Message, StringComparison.Ordinal);
        Assert.Empty(generating.Queries);
    }
}
