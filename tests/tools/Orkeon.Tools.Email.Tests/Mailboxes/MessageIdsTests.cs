using Orkeon.Tools.Email.Mailboxes;

namespace Orkeon.Tools.Email.Tests.Mailboxes;

/// <summary>The opaque message ids the tools hand out and take back.</summary>
public sealed class MessageIdsTests
{
    [Theory]
    [InlineData("INBOX", 1u, 42u)]
    [InlineData("Boîte de réception/Archives 2026", 3857529045u, 7u)]
    [InlineData("[Gmail]/All Mail", 11u, 4294967295u)]
    [InlineData("Weird:folder:with:colons", 5u, 1u)]
    public void Should_round_trip_an_IMAP_id_whatever_the_folder_name(string folder, uint validity, uint uid)
    {
        var id = MessageIds.Imap(folder, validity, uid);

        Assert.StartsWith("imap:", id, StringComparison.Ordinal);
        Assert.Equal(3, id.Count(c => c == ':'));
        Assert.Equal(new ImapMessageId(folder, validity, uid), MessageIds.ParseImap(id));
    }

    [Fact]
    public void Should_round_trip_Graph_and_POP3_ids()
    {
        Assert.Equal("AAMkAD=+/", MessageIds.ParseGraph(MessageIds.Graph("AAMkAD=+/")));
        Assert.Equal("uidl-0001:x", MessageIds.ParsePop3(MessageIds.Pop3("uidl-0001:x")));
    }

    [Theory]
    [InlineData("imap:SU5CT1g:1")]
    [InlineData("imap:SU5CT1g:1:0")]
    [InlineData("imap:SU5CT1g:x:5")]
    [InlineData("imap:SU5CT1g:-1:5")]
    [InlineData("imap::1:5")]
    [InlineData("imap:!!!:1:5")]
    [InlineData("pop3:abc")]
    [InlineData("42")]
    [InlineData("")]
    public void Should_refuse_anything_that_is_not_an_IMAP_id_it_issued(string id)
    {
        var error = Assert.Throws<EmailToolException>(() => MessageIds.ParseImap(id));

        Assert.Equal(EmailErrorCode.InvalidRequest, error.Code);
        Assert.Equal($"'{id}' is not a message id of this account: pass an id exactly as email_search returned it.", error.Message);
    }

    [Theory]
    [InlineData("graph:")]
    [InlineData("imap:SU5CT1g:1:5")]
    [InlineData("AAMkAD")]
    public void Should_refuse_an_id_without_the_Graph_prefix_or_value(string id)
    {
        Assert.Equal(EmailErrorCode.InvalidRequest, Assert.Throws<EmailToolException>(() => MessageIds.ParseGraph(id)).Code);
    }

    [Theory]
    [InlineData("pop3:")]
    [InlineData("graph:abc")]
    public void Should_refuse_an_id_without_the_POP3_prefix_or_value(string id)
    {
        Assert.Equal(EmailErrorCode.InvalidRequest, Assert.Throws<EmailToolException>(() => MessageIds.ParsePop3(id)).Code);
    }
}
