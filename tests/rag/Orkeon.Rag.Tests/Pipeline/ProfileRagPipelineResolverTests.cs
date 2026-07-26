using Microsoft.Extensions.Configuration;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Options;
using Orkeon.Rag.Factories;
using Orkeon.Rag.Pipeline;
using Orkeon.Rag.Tests.Doubles;

namespace Orkeon.Rag.Tests.Pipeline;

/// <summary>
/// Tests for <see cref="ProfileRagPipelineResolver"/> (RAG-04/C4): one memoized
/// pipeline per profile, <c>default</c> delegating to the host pipeline, unknown
/// names failing loudly with the list of known profiles.
/// </summary>
public class ProfileRagPipelineResolverTests
{
    private static IConfiguration EmptyConfig() => new ConfigurationBuilder().Build();

    private static ProfileRagPipelineResolver CreateResolver(
        List<RagOptions> built,
        FakeRagPipeline? defaultPipeline = null)
        => new(
            EmptyConfig(),
            options =>
            {
                built.Add(options);
                return new FakeRagPipeline();
            },
            () => defaultPipeline ?? new FakeRagPipeline());

    [Fact]
    public void Resolve_KnownProfile_BuildsFromItsPreset()
    {
        var built = new List<RagOptions>();
        var resolver = CreateResolver(built);

        resolver.Resolve("balanced");

        var options = Assert.Single(built);
        Assert.Equal("balanced", options.Profile);
        Assert.True(options.Rerank.Enabled);
    }

    [Fact]
    public void Resolve_SameProfile_IsMemoized_CaseInsensitively()
    {
        var built = new List<RagOptions>();
        var resolver = CreateResolver(built);

        var first = resolver.Resolve("quality");
        var second = resolver.Resolve("  QUALITY ");

        Assert.Same(first, second);
        Assert.Single(built); // the factory ran once
    }

    [Fact]
    public void Resolve_DistinctProfiles_BuildDistinctPipelines()
    {
        var built = new List<RagOptions>();
        var resolver = CreateResolver(built);

        var fast = resolver.Resolve("fast");
        var balanced = resolver.Resolve("balanced");

        Assert.NotSame(fast, balanced);
        Assert.Equal(2, built.Count);
    }

    [Fact]
    public void Resolve_Default_ReturnsTheHostPipeline_WithoutBuilding()
    {
        var built = new List<RagOptions>();
        var hostPipeline = new FakeRagPipeline();
        var resolver = CreateResolver(built, hostPipeline);

        Assert.Same(hostPipeline, resolver.Resolve("default"));
        Assert.Same(hostPipeline, resolver.Resolve("DEFAULT"));
        Assert.Empty(built);
    }

    [Fact]
    public void Resolve_UnknownProfile_FailsLoudly_ListingKnownProfiles()
    {
        var resolver = CreateResolver([]);

        var ex = Assert.Throws<RagComponentNotFoundException>(() => resolver.Resolve("warp"));

        Assert.Contains("warp", ex.Message, StringComparison.Ordinal);
        Assert.Contains("fast", ex.Message, StringComparison.Ordinal);
        Assert.Contains("balanced", ex.Message, StringComparison.Ordinal);
        Assert.Contains("quality", ex.Message, StringComparison.Ordinal);
        Assert.Contains("adaptive", ex.Message, StringComparison.Ordinal);
        Assert.Contains("default", ex.Message, StringComparison.Ordinal);
    }

    // ── adaptive profile (RAG-05/C3) ───────────────────────────────────────

    private static ProfileRagPipelineResolver CreateAdaptiveCapableResolver(
        List<RagOptions> built,
        StubQueryComplexityClassifier? classifier = null)
        => new(
            EmptyConfig(),
            options =>
            {
                built.Add(options);
                return new FakeRagPipeline();
            },
            () => new FakeRagPipeline(),
            () => classifier ?? new StubQueryComplexityClassifier(),
            () => new FakeChatClient());

    [Fact]
    public void Resolve_Adaptive_BuildsTheRoutingPipeline_AndMemoizesIt()
    {
        var built = new List<RagOptions>();
        var resolver = CreateAdaptiveCapableResolver(built);

        var first = resolver.Resolve("adaptive");
        var second = resolver.Resolve(" ADAPTIVE ");

        Assert.IsType<AdaptiveRagPipeline>(first);
        Assert.Same(first, second);
        Assert.Empty(built); // no staged pipeline was expanded for adaptive itself
    }

    [Fact]
    public async Task Resolve_Adaptive_DelegatesSingleShotToTheMemoizedBalancedPipeline()
    {
        var built = new List<RagOptions>();
        var classifier = new StubQueryComplexityClassifier
        {
            Route = Orkeon.Rag.Abstractions.QueryRoute.SingleShot,
        };
        var resolver = CreateAdaptiveCapableResolver(built, classifier);

        var adaptive = resolver.Resolve("adaptive");
        await adaptive.QueryAsync(
            new Orkeon.Rag.Abstractions.Models.RagQuery { Text = "q?", Collection = "kb" },
            TestContext.Current.CancellationToken);

        // The SingleShot route lazily expanded exactly the balanced preset.
        var options = Assert.Single(built);
        Assert.Equal("balanced", options.Profile);
    }

    [Fact]
    public void Resolve_Adaptive_WithoutClassifierOrChatClient_FailsLoudly()
    {
        var resolver = CreateResolver([]); // no classifier/chat accessors

        var ex = Assert.Throws<InvalidOperationException>(() => resolver.Resolve("adaptive"));

        Assert.Contains("adaptive", ex.Message, StringComparison.Ordinal);
        Assert.Contains("IQueryComplexityClassifier", ex.Message, StringComparison.Ordinal);
        Assert.Contains("AddOrkeonRag", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_AppliesConfigurationOverrides_OnTopOfThePreset()
    {
        var built = new List<RagOptions>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orkeon:Rag:Rerank:TopN"] = "9",
            })
            .Build();
        var resolver = new ProfileRagPipelineResolver(
            configuration,
            options =>
            {
                built.Add(options);
                return new FakeRagPipeline();
            },
            () => new FakeRagPipeline());

        resolver.Resolve("balanced");

        var options = Assert.Single(built);
        Assert.Equal(9, options.Rerank.TopN); // override
        Assert.Equal("onnx", options.Rerank.Kind); // preset
    }
}
