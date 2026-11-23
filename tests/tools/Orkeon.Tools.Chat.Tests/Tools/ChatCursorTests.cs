using System.Text;
using Orkeon.Tools.Chat.Constants;

namespace Orkeon.Tools.Chat.Tests.Tools;

/// <summary>A cursor comes back from a model: only paging parameters of its own place are taken from it.</summary>
public sealed class ChatCursorTests
{
    [Fact]
    public void Should_come_back_as_it_was_given()
    {
        var cursor = new ChatCursor("chat:19:x@thread.v2", 7, "$skiptoken=a%2Fb", 3);

        Assert.Equal(cursor, ChatCursor.Decode(" " + cursor.Encode() + " ", cursor.Place, 20));
        Assert.Matches("^[A-Za-z0-9_-]+$", cursor.Encode());
        Assert.Equal(new ChatCursor("p", 20, null, 0), ChatCursor.Decode(null, "p", 20));
    }

    [Theory]
    [InlineData("""{"Place":"other","Top":7,"Page":null,"Offset":0}""")]
    [InlineData("""{"Place":"p","Top":0,"Page":null,"Offset":0}""")]
    [InlineData("""{"Place":"p","Top":51,"Page":null,"Offset":0}""")]
    [InlineData("""{"Place":"p","Top":7,"Page":null,"Offset":-1}""")]
    [InlineData("""{"Place":"p","Top":7,"Page":"$filter=x","Offset":0}""")]
    [InlineData("""{"Place":"p","Top":7,"Page":"$skiptoken=x&$expand=y","Offset":0}""")]
    [InlineData("""{"Place":"p","Top":7,"Page":"","Offset":0}""")]
    [InlineData("""{"Place":"p","Top":7,"Page":"https://evil.test/?$skiptoken=x","Offset":0}""")]
    [InlineData("""{"Place":null,"Top":7,"Page":null,"Offset":0}""")]
    [InlineData("""{"Place":"p","Top":"x"}""")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void Should_refuse_a_forged_cursor(string json) =>
        Refused(Convert.ToBase64String(Encoding.UTF8.GetBytes(json)));

    [Theory]
    [InlineData("%%%")]
    [InlineData("a")]
    public void Should_refuse_what_is_not_a_cursor(string text) => Refused(text);

    [Fact]
    public void Should_refuse_an_endless_cursor() =>
        Refused(new ChatCursor("p", ChatDefaults.MaxPage, "$skiptoken=" + new string('a', 9000), 0).Encode());

    private static void Refused(string cursor)
    {
        var error = Assert.Throws<ChatToolException>(() => ChatCursor.Decode(cursor, "p", 20));

        Assert.Equal(ChatErrorCode.InvalidRequest, error.Code);
        Assert.StartsWith("`cursor` is not one this call returned", error.Message, StringComparison.Ordinal);
    }
}
