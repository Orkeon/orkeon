using Orkeon.Infrastructure.Memory.LanceDb;

namespace Orkeon.Infrastructure.Tests.Memory.LanceDb;

/// <summary>
/// Tests for <see cref="LanceDbFilterBuilder"/> — the SQL predicates evaluated
/// server-side by LanceDB (DataFusion SQL).
/// </summary>
public class LanceDbFilterBuilderTests
{
    [Fact]
    public void KeyEquals_BuildsIdPredicate()
    {
        Assert.Equal("id = 'key1'", LanceDbFilterBuilder.KeyEquals("key1"));
    }

    [Fact]
    public void KeyEquals_EscapesSingleQuotes()
    {
        Assert.Equal("id = 'O''Brien'", LanceDbFilterBuilder.KeyEquals("O'Brien"));
    }

    [Fact]
    public void FromMetadataFilter_ReturnsNull_WhenFilterIsNullOrEmpty()
    {
        Assert.Null(LanceDbFilterBuilder.FromMetadataFilter(null));
        Assert.Null(LanceDbFilterBuilder.FromMetadataFilter(new Dictionary<string, object>()));
    }

    [Fact]
    public void FromMetadataFilter_MapsSourceToEquality()
    {
        var predicate = LanceDbFilterBuilder.FromMetadataFilter(
            new Dictionary<string, object> { ["source"] = "research" });

        Assert.Equal("source = 'research'", predicate);
    }

    [Fact]
    public void FromMetadataFilter_MapsTagToLike()
    {
        var predicate = LanceDbFilterBuilder.FromMetadataFilter(
            new Dictionary<string, object> { ["tag"] = "ai" });

        Assert.Equal("tags LIKE '%ai%'", predicate);
    }

    [Fact]
    public void FromMetadataFilter_MapsCustomKeyToItsKeyValuePairInTheMetadataJson()
    {
        // GAP-20: the key AND the value, as the stored JSON spells them — "eng" alone also
        // matched an "engineering" team and any other property holding "eng".
        var predicate = LanceDbFilterBuilder.FromMetadataFilter(
            new Dictionary<string, object> { ["team"] = "eng" });

        Assert.Equal("metadata_json LIKE '%\"team\":\"eng\"%'", predicate);
    }

    [Fact]
    public void FromMetadataFilter_SpellsTheValueAsTheStoredJsonDoes()
    {
        // metadata_json is written by System.Text.Json's default encoder: non-ASCII and quotes
        // are escaped there, so they must be in the pattern too.
        var predicate = LanceDbFilterBuilder.FromMetadataFilter(
            new Dictionary<string, object> { ["crew"] = "équipe d'O" });

        Assert.Equal("metadata_json LIKE '%\"crew\":\"\\u00E9quipe d\\u0027O\"%'", predicate);
    }

    [Fact]
    public void FromMemoryFilter_SpellsTheCustomPropertyAsTheStoredJsonDoes()
    {
        var predicate = LanceDbFilterBuilder.FromMemoryFilter(new Orkeon.Domain.Memory.MemoryFilter
        {
            CustomProperties = new Dictionary<string, string> { ["crew"] = "équipe" },
        });

        Assert.Equal("metadata_json LIKE '%\"crew\":\"\\u00E9quipe\"%'", predicate);
    }

    [Fact]
    public void FromMetadataFilter_JoinsMultipleClausesWithAnd()
    {
        var predicate = LanceDbFilterBuilder.FromMetadataFilter(
            new Dictionary<string, object>
            {
                ["source"] = "research",
                ["tags"] = "ai"
            });

        Assert.Equal("source = 'research' AND tags LIKE '%ai%'", predicate);
    }

    [Fact]
    public void FromMetadataFilter_EscapesQuotesInValues()
    {
        var predicate = LanceDbFilterBuilder.FromMetadataFilter(
            new Dictionary<string, object> { ["source"] = "it's" });

        Assert.Equal("source = 'it''s'", predicate);
    }

    [Fact]
    public void EscapeLiteral_DoublesEverySingleQuote()
    {
        Assert.Equal("''a''''b''", LanceDbFilterBuilder.EscapeLiteral("'a''b'"));
    }
}
