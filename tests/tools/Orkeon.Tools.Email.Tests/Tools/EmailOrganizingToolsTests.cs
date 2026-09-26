using Orkeon.Domain.FileSystem;
using Orkeon.Tools.Email.Mailboxes;
using Orkeon.Tools.Email.Tests.Fixtures;

namespace Orkeon.Tools.Email.Tests.Tools;

/// <summary>
/// The organizing tools through the tool protocol: folders, moves, marks, deletes and saved
/// attachments, each gated by its right and by what the backend can do.
/// </summary>
public sealed class EmailOrganizingToolsTests
{
    [Fact]
    public async Task Should_create_a_folder_and_report_it()
    {
        using var fixture = new ToolFixture();

        var result = ToolResults.Success(await fixture.CallAsync("email_create_folder", ("path", "Clients/ACME")));

        Assert.Equal(("full", true), (result["account"], result["created"]));
        var folder = ToolResults.Object(result, "folder");
        Assert.Equal(("Clients/ACME", "ACME", 0, 0), (folder["path"], folder["name"], folder["total"], folder["unread"]));
        Assert.Equal(["Clients/ACME"], fixture.Mailbox().Created);
    }

    [Theory]
    [InlineData("email_create_folder", "reader", "Organize")]
    [InlineData("email_rename_folder", "reader", "Organize")]
    [InlineData("email_move", "reader", "Organize")]
    [InlineData("email_mark", "reader", "Organize")]
    [InlineData("email_delete", "reader", "Delete")]
    [InlineData("email_save_attachment", "organizer", "Read")]
    public async Task Should_refuse_an_account_without_the_right_the_tool_needs(string tool, string account, string right)
    {
        using var fixture = new ToolFixture();

        var error = ToolResults.Failure(await fixture.CallAsync(tool,
            ("account", account), ("path", "A"), ("new_name", "B"), ("ids", ToolResults.Of("id-1")), ("destination", "archive"),
            ("seen", true), ("id", "id-1"), ("directory", "/output/att")));

        Assert.Contains($"does not grant the {right} right", error, StringComparison.Ordinal);
        Assert.Empty(fixture.Mailboxes.MailboxRequests);
    }

    [Theory]
    [InlineData("email_create_folder", "has no folders: switch the account to IMAP (Incoming:Protocol Imap) for that")]
    [InlineData("email_rename_folder", "has no folders: switch the account to IMAP (Incoming:Protocol Imap) for that")]
    [InlineData("email_move", "has no folders to move messages into: switch the account to IMAP (Incoming:Protocol Imap) for that")]
    [InlineData("email_mark", "has no read or flagged marks: switch the account to IMAP (Incoming:Protocol Imap) for that")]
    [InlineData("email_delete", "has no trash and deletes for good: pass `permanent: true` (needs the Purge right)")]
    public async Task Should_refuse_what_the_backend_cannot_do(string tool, string clause)
    {
        using var fixture = new ToolFixture();
        fixture.Mailbox("pop").Capabilities = MailboxCapabilities.None;

        var error = ToolResults.Failure(await fixture.CallAsync(tool,
            ("account", "pop"), ("path", "A"), ("new_name", "B"), ("ids", ToolResults.Of("id-1")), ("destination", "archive"), ("seen", true)));

        Assert.Equal($"Tool execution failed: E-mail account 'pop' reads mail over POP3, which {clause}.", error);
    }

    [Fact]
    public async Task Should_rename_a_folder_and_report_the_previous_path()
    {
        using var fixture = new ToolFixture();

        var result = ToolResults.Success(await fixture.CallAsync("email_rename_folder", ("path", " /Clients/ACME/ "), ("new_name", "Acme")));

        Assert.Equal("Clients/ACME", result["previous_path"]);
        Assert.Equal("Clients/Acme", ToolResults.Object(result, "folder")["path"]);
        Assert.Equal([(" /Clients/ACME/ ", "Acme")], fixture.Mailbox().Renamed);
    }

    [Theory]
    [InlineData("email_rename_folder", "path", "`path` is required")]
    [InlineData("email_rename_folder", "new_name", "`new_name` is required")]
    [InlineData("email_create_folder", "path", "`path` is required, e.g. Clients/ACME")]
    [InlineData("email_move", "destination", "`destination` is required: a folder path or role")]
    public async Task Should_explain_a_blank_required_argument(string tool, string blank, string message)
    {
        using var fixture = new ToolFixture();
        var parameters = new Dictionary<string, object?> { ["path"] = "A", ["new_name"] = "B", ["ids"] = ToolResults.Of("id-1"), ["destination"] = "archive" };
        parameters[blank] = " ";

        var error = ToolResults.Failure(await fixture.CallAsync(tool, [.. parameters.Select(p => (p.Key, p.Value))]));

        Assert.Equal(message, error);
    }

    [Fact]
    public async Task Should_move_messages_and_return_their_new_ids()
    {
        using var fixture = new ToolFixture();

        var result = ToolResults.Success(await fixture.CallAsync("email_move", ("ids", ToolResults.Of(" id-1 ", "", "id-2")), ("destination", " archive ")));

        Assert.Equal("archive", result["destination"]);
        var moved = ToolResults.Objects(result, "moved");
        Assert.Equal([("id-1", "id-1-moved"), ("id-2", "id-2-moved")], moved.Select(m => ((string)m["id"]!, (string)m["new_id"]!)));
        var (ids, destination) = Assert.Single(fixture.Mailbox().Moves);
        Assert.Equal(["id-1", "id-2"], ids);
        Assert.Equal("archive", destination);
    }

    [Theory]
    [InlineData("email_move")]
    [InlineData("email_mark")]
    [InlineData("email_delete")]
    public async Task Should_require_at_least_one_message_id(string tool)
    {
        using var fixture = new ToolFixture();

        var error = ToolResults.Failure(await fixture.CallAsync(tool, ("ids", ToolResults.Of(" ", "")), ("destination", "archive"), ("seen", true)));

        Assert.Equal("Tool execution failed: `ids` needs at least one message id from email_search.", error);
    }

    [Fact]
    public async Task Should_set_the_marks_asked_for()
    {
        using var fixture = new ToolFixture();

        var result = ToolResults.Success(await fixture.CallAsync("email_mark", ("ids", ToolResults.Of("id-1", "id-2")), ("flagged", false)));

        Assert.Equal(2, result["updated"]);
        var update = Assert.Single(fixture.Mailbox().FlagUpdates);
        Assert.Null(update.Seen);
        Assert.False(update.Flagged);
    }

    [Fact]
    public async Task Should_ask_for_a_mark_When_none_is_given()
    {
        using var fixture = new ToolFixture();

        var error = ToolResults.Failure(await fixture.CallAsync("email_mark", ("ids", ToolResults.Of("id-1"))));

        Assert.Equal("pass `seen`, `flagged` or both", error);
        Assert.Empty(fixture.Mailbox().FlagUpdates);
    }

    [Fact]
    public async Task Should_move_to_the_trash_by_default()
    {
        using var fixture = new ToolFixture();

        var result = ToolResults.Success(await fixture.CallAsync("email_delete", ("ids", ToolResults.Of("id-1"))));

        Assert.Equal((1, false, "Trash"), (result["deleted"], result["permanent"], result["moved_to"]));
        Assert.False(Assert.Single(fixture.Mailbox().Deletes).Permanent);
    }

    [Fact]
    public async Task Should_require_the_Purge_right_but_no_trash_to_delete_for_good()
    {
        using var fixture = new ToolFixture();
        fixture.Mailbox().Capabilities = MailboxCapabilities.None;
        fixture.Options.Accounts["deleter"] = TestAccounts.Custom(Orkeon.Tools.Email.Configuration.EmailRights.Delete);

        var purged = ToolResults.Success(await fixture.CallAsync("email_delete", ("ids", ToolResults.Of("id-1")), ("permanent", true)));
        var refused = ToolResults.Failure(await fixture.CallAsync("email_delete", ("account", "deleter"), ("ids", ToolResults.Of("id-1")), ("permanent", true)));

        Assert.Equal((1, true), (purged["deleted"], purged["permanent"]));
        Assert.False(purged.ContainsKey("moved_to"));
        Assert.Contains("does not grant the Purge right", refused, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_save_every_attachment_with_a_safe_name_that_never_overwrites()
    {
        using var fixture = new ToolFixture();
        fixture.Mailbox().Add("id-1", MimeSamples.WithAttachment(fileName: "../../report.pdf"));
        fixture.Files.AddFile("/output/att/report.pdf", [1, 2, 3]);

        var result = ToolResults.Success(await fixture.CallAsync("email_save_attachment", ("id", "id-1"), ("directory", "/output/att/")));

        var saved = Assert.Single(ToolResults.Objects(result, "saved"));
        Assert.Equal((0, "report (1).pdf", "/output/att/report (1).pdf"), (saved["index"], saved["file_name"], saved["path"]));
        Assert.Equal(MimeSamples.PdfBytes, await fixture.Files.TryReadAllBytesAsync("/output/att/report (1).pdf", TestContext.Current.CancellationToken));
        Assert.Equal([1, 2, 3], await fixture.Files.TryReadAllBytesAsync("/output/att/report.pdf", TestContext.Current.CancellationToken));
        Assert.Contains(("/output/att/_", FileAccessRights.Write | FileAccessRights.Create), fixture.Files.Validations);
        Assert.Equal(("full", "id-1"), (result["account"], result["id"]));
    }

    [Fact]
    public async Task Should_save_only_the_attachment_at_the_given_index()
    {
        using var fixture = new ToolFixture();
        fixture.Mailbox().Add("id-rich", MimeSamples.RichText());

        var result = ToolResults.Success(await fixture.CallAsync("email_save_attachment", ("id", "id-rich"), ("directory", "/output"), ("index", 1)));

        Assert.Equal("/output/report.pdf", Assert.Single(ToolResults.Objects(result, "saved"))["path"]);
        Assert.False(await fixture.Files.ExistsAsync("/output/logo.png", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Should_refuse_a_directory_it_may_not_write_before_fetching_anything()
    {
        using var fixture = new ToolFixture();
        fixture.Mailbox().Add("id-1", MimeSamples.WithAttachment());

        var readOnly = ToolResults.Failure(await fixture.CallAsync("email_save_attachment", ("id", "id-1"), ("directory", "/workspace/att")));
        var nowhere = ToolResults.Failure(await fixture.CallAsync("email_save_attachment", ("id", "id-1"), ("directory", "/etc")));

        Assert.Equal("Tool execution failed: Cannot write into '/workspace/att': The mount '/workspace' does not grant the right this needs.", readOnly);
        Assert.StartsWith("Tool execution failed: Cannot write into '/etc': No mount", nowhere, StringComparison.Ordinal);
        Assert.Empty(fixture.Mailbox().Fetched);
    }

    [Fact]
    public async Task Should_refuse_a_directory_where_it_may_write_but_not_create_files()
    {
        using var fixture = new ToolFixture();
        fixture.Files.AddMount("/drop", FileAccessRights.Read | FileAccessRights.Write);
        fixture.Mailbox().Add("id-1", MimeSamples.WithAttachment());

        var refused = ToolResults.Failure(await fixture.CallAsync("email_save_attachment", ("id", "id-1"), ("directory", "/drop")));

        Assert.Equal("Tool execution failed: Cannot write into '/drop': The mount '/drop' does not grant the right this needs.", refused);
        Assert.Empty(fixture.Mailbox().Fetched);
    }

    [Fact]
    public async Task Should_explain_a_missing_attachment()
    {
        using var fixture = new ToolFixture();
        fixture.Mailbox().Add("plain", MimeSamples.Plain()).Add("id-1", MimeSamples.WithAttachment());

        var none = ToolResults.Failure(await fixture.CallAsync("email_save_attachment", ("id", "plain"), ("directory", "/output")));
        var wrongIndex = ToolResults.Failure(await fixture.CallAsync("email_save_attachment", ("id", "id-1"), ("directory", "/output"), ("index", 5)));

        Assert.Equal("Tool execution failed: This message has no attachment.", none);
        Assert.Equal("Tool execution failed: This message has no attachment at index 5: see email_read.", wrongIndex);
    }
}
