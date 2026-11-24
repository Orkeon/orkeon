using Orkeon.Tools.Chat.Providers;
using Orkeon.Tools.Chat.Providers.Teams;
using Orkeon.Tools.Chat.Tests.Fixtures;

namespace Orkeon.Tools.Chat.Tests.Providers;

/// <summary>The query a search sends: whatever a model writes stays inside its quotes.</summary>
public sealed class GraphSearchTests
{
    [Theory]
    [InlineData("plain", "\"plain\"")]
    [InlineData("a\" OR from:bob", "\"a OR from:bob\"")]
    [InlineData("a“ OR ”b", "\"a OR b\"")]
    [InlineData("a＂b", "\"a b\"")]
    [InlineData("build*", "\"build\"")]
    [InlineData("(x)", "\"x\"")]
    [InlineData("a\\\"b", "\"a b\"")]
    [InlineData("a\u0000\nb", "\"a b\"")]
    [InlineData("sent>2022", "\"sent 2022\"")]
    [InlineData("été", "\"été\"")]
    [InlineData("NOT", "\"NOT\"")]
    [InlineData("\"\"", "")]
    public void Should_keep_a_word_inside_its_quotes(string word, string expected) =>
        Assert.Equal(expected, GraphSearch.QueryString(new ChatSearchQuery([word], null, null, 0, 25)));

    [Fact]
    public void Should_put_the_criteria_side_by_side() =>
        Assert.Equal(
            "\"red\" \"build\" from:\"ada@example.org\" mentions:" + Samples.Self.Replace("-", "", StringComparison.Ordinal),
            GraphSearch.QueryString(new ChatSearchQuery(["red", "build"], "ada@example.org", Samples.Self, 0, 25)));
}
