using Orkeon.Tools.Email.Accounts;
using Orkeon.Tools.Email.Administration;
using Orkeon.Tools.Email.Configuration;

namespace Orkeon.Tools.Email.Tests.Accounts;

/// <summary>Which mail rights a crew's tools need that no ready account grants.</summary>
public sealed class EmailRightsCheckTests : IDisposable
{
    private readonly Fixtures.ToolFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static EmailAccountStatus Status(string rights, int allowedRecipients = 0, bool ready = true)
        => new()
        {
            Name = "perso",
            Provider = "Gmail",
            Rights = rights,
            AllowedRecipients = allowedRecipients,
            Ready = ready,
        };

    [Fact]
    [Trait("BR", "BR-06")]
    public void ShouldPass_WhenAReadyAccountGrantsEveryRightTheToolsNeed()
    {
        var lines = new EmailRightsCheck(_fixture.Administration).Check(
            ["email_search", "email_move", "email_send", "file_read"],
            [Status("Read, Organize, Send", allowedRecipients: 1)]);

        Assert.Empty(lines);
    }

    [Fact]
    [Trait("BR", "BR-06")]
    public void ShouldPass_WhenTheCrewNamesNoMailTool()
    {
        var lines = new EmailRightsCheck(_fixture.Administration).Check(["file_read", "email_parser"], []);

        Assert.Empty(lines);
    }

    [Fact]
    [Trait("BR", "BR-06")]
    public void ShouldNameToolRightAndWhere_WhenNoAccountIsConfigured()
    {
        var lines = new EmailRightsCheck(_fixture.Administration).Check(["email_send", "email_read"], []);

        Assert.Equal(2, lines.Count);
        var send = Assert.Single(lines, l => l.Contains("email_send", StringComparison.Ordinal));
        Assert.Contains("Send", send, StringComparison.Ordinal);
        Assert.Contains("Settings › E-mail", send, StringComparison.Ordinal);
        Assert.Contains("Orkeon:Tools:Email:Accounts", send, StringComparison.Ordinal);
        var read = Assert.Single(lines, l => l.Contains("email_read", StringComparison.Ordinal));
        Assert.Contains("Read", read, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "BR-06")]
    public void ShouldRefuseEmailSend_WhenTheSendRightHasNoAllowedRecipients()
    {
        var lines = new EmailRightsCheck(_fixture.Administration).Check(["email_send"], [Status("Read, Send", allowedRecipients: 0)]);

        var line = Assert.Single(lines);
        Assert.Contains("email_send", line, StringComparison.Ordinal);
        Assert.Contains("AllowedRecipients", line, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "BR-06")]
    public void ShouldIgnoreAnAccountThatIsNotReady()
    {
        var lines = new EmailRightsCheck(_fixture.Administration).Check(
            ["email_read"], [Status("Read, Organize, Draft, Send, Delete, Purge", allowedRecipients: 3, ready: false)]);

        var line = Assert.Single(lines);
        Assert.Contains("email_read", line, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("BR", "BR-06")]
    [InlineData("email_accounts", "None")]
    [InlineData("email_folders", "Read")]
    [InlineData("email_search", "Read")]
    [InlineData("email_read", "Read")]
    [InlineData("email_save_attachment", "Read")]
    [InlineData("email_create_folder", "Organize")]
    [InlineData("email_rename_folder", "Organize")]
    [InlineData("email_move", "Organize")]
    [InlineData("email_mark", "Organize")]
    [InlineData("email_delete", "Delete")]
    [InlineData("email_draft", "Draft")]
    [InlineData("email_send", "Send")]
    [InlineData("email_parser", "None")]
    public void ShouldMapEachOfTheTwelveToolsToItsBaseRight(string tool, string expected)
    {
        Assert.Equal(Enum.Parse<EmailRights>(expected), EmailRightsCheck.BaseRight(tool));
    }
}
