using Orkeon.Domain.Memory;

namespace Orkeon.Domain.Tests.Memory;

/// <summary>
/// Tests for <see cref="MemoryFilter"/> (RAG-02/C4): typed matching semantics and
/// conversions from/to the legacy dictionary filter shape.
/// </summary>
public class MemoryFilterTests
{
    private static readonly string[] AlphaBeta = ["alpha", "beta"];

    private static MemoryItem CreateItem(
        string content = "content",
        string? source = null,
        IReadOnlyList<string>? tags = null,
        Dictionary<string, string>? customProperties = null)
        => MemoryItem.Create(content, source: source, tags: tags, customProperties: customProperties);

    // ── IsEmpty ───────────────────────────────────────────────────────────

    [Fact]
    public void IsEmpty_NoCriteria_IsTrue()
    {
        Assert.True(new MemoryFilter().IsEmpty);
        Assert.True(new MemoryFilter { Tags = [], CustomProperties = new Dictionary<string, string>() }.IsEmpty);
    }

    [Fact]
    public void IsEmpty_AnyCriterion_IsFalse()
    {
        Assert.False(new MemoryFilter { Source = "docs" }.IsEmpty);
        Assert.False(new MemoryFilter { Tags = ["a"] }.IsEmpty);
        Assert.False(new MemoryFilter { CustomProperties = new Dictionary<string, string> { ["k"] = "v" } }.IsEmpty);
    }

    // ── Matches ───────────────────────────────────────────────────────────

    [Fact]
    public void Matches_NullItem_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new MemoryFilter().Matches(null!));
    }

    [Fact]
    public void Matches_EmptyFilter_MatchesAnyItem()
    {
        Assert.True(new MemoryFilter().Matches(CreateItem()));
    }

    [Fact]
    public void Matches_Source_IsCaseInsensitiveEquality()
    {
        var item = CreateItem(source: "Wiki");

        Assert.True(new MemoryFilter { Source = "wiki" }.Matches(item));
        Assert.False(new MemoryFilter { Source = "web" }.Matches(item));
    }

    [Fact]
    public void Matches_Tags_RequiresAllTags_CaseInsensitive()
    {
        var item = CreateItem(tags: ["Alpha", "beta"]);

        Assert.True(new MemoryFilter { Tags = ["alpha"] }.Matches(item));
        Assert.True(new MemoryFilter { Tags = ["alpha", "BETA"] }.Matches(item));
        Assert.False(new MemoryFilter { Tags = ["alpha", "gamma"] }.Matches(item));
    }

    [Fact]
    public void Matches_CustomProperties_RequiresAllPairs()
    {
        var item = CreateItem(customProperties: new Dictionary<string, string>
        {
            ["collection"] = "docs",
            ["lang"] = "fr"
        });

        Assert.True(new MemoryFilter
        {
            CustomProperties = new Dictionary<string, string> { ["collection"] = "DOCS" }
        }.Matches(item));

        Assert.False(new MemoryFilter
        {
            CustomProperties = new Dictionary<string, string> { ["collection"] = "docs", ["lang"] = "en" }
        }.Matches(item));

        Assert.False(new MemoryFilter
        {
            CustomProperties = new Dictionary<string, string> { ["missing"] = "x" }
        }.Matches(item));
    }

    [Fact]
    public void Matches_CustomProperties_ItemWithoutProperties_DoesNotMatch()
    {
        var filter = new MemoryFilter
        {
            CustomProperties = new Dictionary<string, string> { ["k"] = "v" }
        };

        Assert.False(filter.Matches(CreateItem()));
    }

    [Fact]
    public void Matches_CombinedCriteria_AllMustHold()
    {
        var item = CreateItem(source: "wiki", tags: ["alpha"]);

        Assert.True(new MemoryFilter { Source = "wiki", Tags = ["alpha"] }.Matches(item));
        Assert.False(new MemoryFilter { Source = "wiki", Tags = ["beta"] }.Matches(item));
        Assert.False(new MemoryFilter { Source = "web", Tags = ["alpha"] }.Matches(item));
    }

    // ── FromDictionary ────────────────────────────────────────────────────

    [Fact]
    public void FromDictionary_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(MemoryFilter.FromDictionary(null));
        Assert.Null(MemoryFilter.FromDictionary(new Dictionary<string, object>()));
    }

    [Fact]
    public void FromDictionary_MapsSourceTagAndCustomKeys()
    {
        var filter = MemoryFilter.FromDictionary(new Dictionary<string, object>
        {
            ["source"] = "wiki",
            ["tag"] = "alpha",
            ["collection"] = "docs"
        });

        Assert.NotNull(filter);
        Assert.Equal("wiki", filter.Source);
        Assert.Equal(["alpha"], filter.Tags);
        Assert.NotNull(filter.CustomProperties);
        Assert.Equal("docs", filter.CustomProperties["collection"]);
    }

    [Fact]
    public void FromDictionary_KeysAreCaseInsensitive()
    {
        var filter = MemoryFilter.FromDictionary(new Dictionary<string, object>
        {
            ["Source"] = "wiki",
            ["TAGS"] = "alpha"
        });

        Assert.NotNull(filter);
        Assert.Equal("wiki", filter.Source);
        Assert.Equal(["alpha"], filter.Tags);
        Assert.Null(filter.CustomProperties);
    }

    [Fact]
    public void FromDictionary_TagsAsSequence_AreAllCollected()
    {
        var filter = MemoryFilter.FromDictionary(new Dictionary<string, object>
        {
            ["tags"] = AlphaBeta
        });

        Assert.NotNull(filter);
        Assert.Equal(["alpha", "beta"], filter.Tags);
    }

    [Fact]
    public void FromDictionary_NonStringCustomValue_IsStringified()
    {
        var filter = MemoryFilter.FromDictionary(new Dictionary<string, object>
        {
            ["priority"] = 42
        });

        Assert.NotNull(filter);
        Assert.NotNull(filter.CustomProperties);
        Assert.Equal("42", filter.CustomProperties["priority"]);
    }

    // ── ToDictionary ──────────────────────────────────────────────────────

    [Fact]
    public void ToDictionary_EmptyFilter_ReturnsEmptyDictionary()
    {
        Assert.Empty(new MemoryFilter().ToDictionary());
    }

    [Fact]
    public void ToDictionary_SingleTag_EmitsLegacyStringShape()
    {
        var dict = new MemoryFilter { Source = "wiki", Tags = ["alpha"] }.ToDictionary();

        Assert.Equal("wiki", dict["source"]);
        Assert.Equal("alpha", dict["tags"]);
    }

    [Fact]
    public void ToDictionary_MultipleTags_EmitsStringArray()
    {
        var dict = new MemoryFilter { Tags = ["alpha", "beta"] }.ToDictionary();

        var tags = Assert.IsType<string[]>(dict["tags"]);
        Assert.Equal(["alpha", "beta"], tags);
    }

    [Fact]
    public void ToDictionary_RoundTripsThroughFromDictionary()
    {
        var original = new MemoryFilter
        {
            Source = "wiki",
            Tags = ["alpha", "beta"],
            CustomProperties = new Dictionary<string, string> { ["collection"] = "docs" }
        };

        var roundTripped = MemoryFilter.FromDictionary(original.ToDictionary());

        Assert.NotNull(roundTripped);
        Assert.Equal(original.Source, roundTripped.Source);
        Assert.Equal(original.Tags, roundTripped.Tags);
        Assert.NotNull(roundTripped.CustomProperties);
        Assert.Equal("docs", roundTripped.CustomProperties["collection"]);
    }
}
