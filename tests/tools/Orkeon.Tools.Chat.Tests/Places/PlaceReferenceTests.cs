using Orkeon.Tools.Chat.Places;
using Orkeon.Tools.Chat.Providers.Teams;
using Orkeon.Tools.Chat.Tests.Fixtures;

namespace Orkeon.Tools.Chat.Tests.Places;

/// <summary>The one syntax of places, and the shapes of the ids they carry.</summary>
public sealed class PlaceReferenceTests
{
    [Theory]
    [InlineData("*", "Any", "*", "")]
    [InlineData(" team:Platform ", "Team", "Platform", "")]
    [InlineData("TEAM:R&D / ops", "Team", "R&D / ops", "")]
    [InlineData("team:Platform/channel:builds", "Channel", "Platform", "builds")]
    [InlineData("team: Platform /channel: release notes ", "Channel", "Platform", "release notes")]
    [InlineData("chat:19:abc@thread.v2", "Chat", "19:abc@thread.v2", "")]
    [InlineData("user:ada@example.org", "User", "ada@example.org", "")]
    [InlineData("user:22222222-2222-2222-2222-222222222222", "User", "22222222-2222-2222-2222-222222222222", "")]
    [InlineData("user:gus_partner.test#EXT#@example.org", "User", "gus_partner.test#EXT#@example.org", "")]
    [InlineData("user:*@example.org", "Domain", "example.org", "")]
    public void Should_read_a_place(string text, string form, string value, string channel)
    {
        Assert.True(PlaceReference.TryParse(text, out var place));
        Assert.Equal((Enum.Parse<PlaceForm>(form), value, channel), (place.Form, place.Value, place.Channel));
        Assert.Equal(text.Trim(), place.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("Platform")]
    [InlineData("team:")]
    [InlineData("team:*")]
    [InlineData("team:*/channel:builds")]
    [InlineData("team:Platform/channel:")]
    [InlineData("team:Platform/channel:*")]
    [InlineData("team:/channel:builds")]
    [InlineData("chat:")]
    [InlineData("chat:*")]
    [InlineData("chat:../me")]
    [InlineData("chat:a/b")]
    [InlineData("chat:a?b")]
    [InlineData("chat:a%2fb")]
    [InlineData("user:")]
    [InlineData("user:*")]
    [InlineData("user:Ada")]
    [InlineData("user:ada@")]
    [InlineData("user:@example.org")]
    [InlineData("user:a@b@example.org")]
    [InlineData("user:a/b@example.org")]
    [InlineData("user:*@")]
    [InlineData("user:*@org")]
    [InlineData("user:*@*.org")]
    [InlineData("user:*@example.org.")]
    [InlineData("user:ad*@example.org")]
    [InlineData("team:a\nb")]
    public void Should_refuse_what_is_not_a_place(string? text) => Assert.False(PlaceReference.TryParse(text, out _));

    [Fact]
    public void Should_refuse_a_place_that_has_no_end() => Assert.False(PlaceReference.TryParse("team:" + new string('a', 1000), out _));

    [Fact]
    public void Should_tell_the_shapes_of_ids()
    {
        Assert.True(ChatIds.IsDirectoryId(Samples.Ada));
        Assert.False(ChatIds.IsDirectoryId(Samples.Ada.Replace("-", string.Empty, StringComparison.Ordinal)));
        Assert.True(ChatIds.IsChannelId(Samples.Builds));
        Assert.False(ChatIds.IsChannelId("builds"));
        Assert.True(ChatIds.IsConversationId(Samples.Direct));
        Assert.False(ChatIds.IsConversationId("19:a/../b@thread.v2"));
        Assert.True(ChatIds.IsMessageId("1616965872395"));
        Assert.False(ChatIds.IsMessageId("1/replies"));
        Assert.False(ChatIds.IsMessageId(string.Empty));
        Assert.False(ChatIds.IsMessageId(new string('1', 21)));
    }

    [Theory]
    [InlineData("a/b", "a%2Fb")]
    [InlineData("19:x@thread.v2", "19%3Ax%40thread.v2")]
    [InlineData("a?b#c%", "a%3Fb%23c%25")]
    public void Should_escape_an_id_as_one_path_segment(string id, string segment) => Assert.Equal(segment, GraphClient.Segment(id));
}
