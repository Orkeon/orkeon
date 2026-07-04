using Microsoft.Extensions.Options;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory.Cognitive;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Infrastructure.Tests.CovMisc;

/// <summary>
/// Coverage for <see cref="ContradictionDetector"/> error handling and the JSON / regex
/// fallback parsing paths (<c>ParseCheck</c>) not exercised by the happy-path suite.
/// </summary>
public class CovMisc_ContradictionDetectorTests
{
    private static ContradictionDetector CreateDetector(MockLlmProvider provider)
        => new(provider, Options.Create(new CognitiveMemoryOptions()), new MockLogger<ContradictionDetector>());

    private static List<MemoryItem> OneMemory()
        => [MemoryItem.Create("Existing fact", importance: 0.5f)];

    [Fact]
    public void Constructor_NullProvider_Throws()
        => Assert.Throws<ArgumentNullException>(() =>
            new ContradictionDetector(null!, Options.Create(new CognitiveMemoryOptions()), new MockLogger<ContradictionDetector>()));

    [Fact]
    public void Constructor_NullOptions_Throws()
        => Assert.Throws<ArgumentNullException>(() =>
            new ContradictionDetector(new MockLlmProvider(), null!, new MockLogger<ContradictionDetector>()));

    [Fact]
    public void Constructor_NullLogger_Throws()
        => Assert.Throws<ArgumentNullException>(() =>
            new ContradictionDetector(new MockLlmProvider(), Options.Create(new CognitiveMemoryOptions()), null!));

    [Fact]
    public async Task CheckAsync_LlmThrows_ReturnsNoContradiction()
    {
        var provider = new MockLlmProvider();
        provider.SetChatException(new InvalidOperationException("llm error"));
        var detector = CreateDetector(provider);

        var result = await detector.CheckAsync("new content", OneMemory(), CancellationToken.None);

        Assert.False(result.HasContradiction);
    }

    [Fact]
    public async Task CheckAsync_KeepExistingAction_IsParsed()
    {
        var provider = new MockLlmProvider();
        provider.SetChatResult("""
            {"has_contradiction": true, "conflicting_ids": ["id1","id2"], "description": "conflict", "resolution": "drop new", "action": "keep_existing"}
            """);
        var detector = CreateDetector(provider);

        var result = await detector.CheckAsync("new content", OneMemory(), CancellationToken.None);

        Assert.True(result.HasContradiction);
        Assert.Equal(ConflictResolution.KeepExisting, result.RecommendedAction);
        Assert.Equal(2, result.ConflictingMemoryIds.Count);
        Assert.Equal("conflict", result.Description);
        Assert.Equal("drop new", result.Resolution);
    }

    [Fact]
    public async Task CheckAsync_JsonEmbeddedInProse_IsExtracted()
    {
        var provider = new MockLlmProvider();
        provider.SetChatResult("""
            Sure, here is the analysis:
            {"has_contradiction": false, "action": "keep_both"}
            Hope that helps!
            """);
        var detector = CreateDetector(provider);

        var result = await detector.CheckAsync("new content", OneMemory(), CancellationToken.None);

        Assert.False(result.HasContradiction);
        Assert.Equal(ConflictResolution.KeepBoth, result.RecommendedAction);
    }

    // --- Direct ParseCheck coverage (internal, accessible via InternalsVisibleTo) ---

    [Fact]
    public void ParseCheck_BooleanAsString_IsCoerced()
    {
        var detector = CreateDetector(new MockLlmProvider());

        var result = detector.ParseCheck("""{"has_contradiction": "true", "action": "merge"}""");

        Assert.True(result.HasContradiction);
        Assert.Equal(ConflictResolution.Merge, result.RecommendedAction);
    }

    [Fact]
    public void ParseCheck_NoJsonBraces_UsesFallbackRegex()
    {
        var detector = CreateDetector(new MockLlmProvider());

        // No braces at all -> jsonStart < 0 -> FallbackParse path.
        var result = detector.ParseCheck("has_contradiction true and action keep_new");

        // FallbackParse looks for quoted JSON-like fragments; none here so defaults apply.
        Assert.False(result.HasContradiction);
        Assert.Equal(ConflictResolution.KeepBoth, result.RecommendedAction);
    }

    [Fact]
    public void ParseCheck_MalformedJson_FallsBackToRegex()
    {
        var detector = CreateDetector(new MockLlmProvider());

        // Braces present but invalid JSON (trailing comma + no closing brace) triggers
        // JsonException -> FallbackParse, which regex-matches the quoted fields.
        var malformed = """{ "has_contradiction": true, "action": "keep_new", "description": "broken value", """;

        var result = detector.ParseCheck(malformed);

        Assert.True(result.HasContradiction);
        Assert.Equal(ConflictResolution.KeepNew, result.RecommendedAction);
        Assert.Equal("broken value", result.Description);
    }
}
