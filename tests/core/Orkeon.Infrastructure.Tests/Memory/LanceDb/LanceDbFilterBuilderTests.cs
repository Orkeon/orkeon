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
    public void FromMetadataFilter_MapsCustomKeyToMetadataLike()
    {
        var predicate = LanceDbFilterBuilder.FromMetadataFilter(
            new Dictionary<string, object> { ["team"] = "eng" });

        Assert.Equal("metadata_json LIKE '%eng%'", predicate);
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
