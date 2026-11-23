using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.Tests.Fixtures;

namespace Orkeon.Tools.Chat.Tests.Configuration;

/// <summary>The permissions asked for at the sign-in: those of the rights, and nothing more.</summary>
public sealed class ChatScopesTests
{
    private const string Reading = "Team.ReadBasic.All Channel.ReadBasic.All Chat.Read User.ReadBasic.All";
    private const string Sending = "ChannelMessage.Send ChatMessage.Send Chat.Create Team.ReadBasic.All Channel.ReadBasic.All User.ReadBasic.All";

    private const string Changing = "Team.ReadBasic.All Channel.ReadBasic.All User.ReadBasic.All Chat.ReadWrite";

    [Theory]
    [InlineData("Read", "", Reading)]
    [InlineData("Read", "ChannelMessages", Reading + " ChannelMessage.Read.All")]
    [InlineData("Read", "ChannelEdit,Meetings", Reading)]
    [InlineData("Send", "", Sending)]
    [InlineData("Send", "ChannelMessages,ChannelEdit,Meetings", Sending)]
    [InlineData("Read, Send", "", "Team.ReadBasic.All Channel.ReadBasic.All Chat.Read User.ReadBasic.All ChannelMessage.Send ChatMessage.Send Chat.Create")]
    [InlineData("Edit", "", Changing)]
    [InlineData("Edit", "ChannelEdit", Changing + " ChannelMessage.ReadWrite")]
    [InlineData("Delete", "", Changing)]
    [InlineData("Delete", "ChannelEdit,ChannelMessages", Changing + " ChannelMessage.Read.All ChannelMessage.ReadWrite")]
    [InlineData("React", "", "ChatMessage.Send User.ReadBasic.All")]
    [InlineData("Meetings", "", "OnlineMeetings.Read")]
    [InlineData("Meetings", "Meetings", "OnlineMeetings.Read OnlineMeetingTranscript.Read.All")]
    [InlineData("Meetings", "ChannelMessages", "OnlineMeetings.Read")]
    public void Should_ask_for_the_permissions_of_the_rights_and_offline_access(string rights, string granted, string permissions)
    {
        var account = TestChatAccounts.Resolve(TestChatAccounts.Teams(
            Enum.Parse<ChatRights>(rights), granted: granted.Split(',', StringSplitOptions.RemoveEmptyEntries)));

        Assert.Equal(
            [.. permissions.Split(' ').Append("offline_access").Order(StringComparer.Ordinal)],
            account.OAuth.Scopes.Order(StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData("ChannelMessage.Read.All", true)]
    [InlineData("ChannelMessage.ReadWrite", true)]
    [InlineData("OnlineMeetingTranscript.Read.All", true)]
    [InlineData("ChannelMember.Read.All", true)]
    [InlineData("Chat.Read", false)]
    [InlineData("User.ReadBasic.All", false)]
    public void Should_know_what_an_administrator_grants(string permission, bool administrator) =>
        Assert.Equal(administrator, ChatScopes.NeedsAdministrator(permission));
}
