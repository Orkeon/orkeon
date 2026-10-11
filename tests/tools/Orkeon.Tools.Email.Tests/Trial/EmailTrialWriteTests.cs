using Orkeon.Tools.Email.Tests.Fixtures;
using Orkeon.Tools.Email.Trial;

namespace Orkeon.Tools.Email.Tests.Trial;

/// <summary>Inside a mail trial every write lands in the trial mailbox and in the journal, never at the provider.</summary>
public sealed class EmailTrialWriteTests
{
    [Fact]
    [Trait("BR", "BR-02")]
    [Trait("BR", "BR-03")]
    public async Task ShouldRecordTheSend_WithRecipientsSubjectAndBody_WhenEmailSendIsCalledInATrial()
    {
        using var fixture = new ToolFixture(scoped: true);
        using var trial = EmailTrial.Enter(TrialMailboxSeed.Sample());

        ToolResults.Success(await fixture.CallAsync(
            "email_send",
            ("to", ToolResults.Of("Client <client@example.com>")),
            ("subject", "Our quote"),
            ("text", "40 chairs: 4,800 EUR.")));

        var action = Assert.Single(trial.Actions);
        Assert.Equal(SimulatedMailActionKind.Send, action.Kind);
        Assert.Equal("full", action.Account);
        Assert.Equal(["client@example.com"], action.Recipients);
        Assert.Equal("Our quote", action.Subject);
        Assert.Contains("4,800 EUR", action.Body, StringComparison.Ordinal);
        Assert.NotEmpty(action.MessageId);
        var found = ToolResults.Success(await fixture.CallAsync("email_search", ("folder", "sent"), ("subject", "Our quote")));
        Assert.Single(ToolResults.Objects(found, "messages"));
        Assert.Empty(fixture.Sender().Sent);
    }

    [Fact]
    [Trait("BR", "BR-02")]
    [Trait("BR", "BR-03")]
    public async Task ShouldRecordTheDraft_AndShowItInTheDraftsFolder()
    {
        using var fixture = new ToolFixture(scoped: true);
        using var trial = EmailTrial.Enter(TrialMailboxSeed.Sample());

        ToolResults.Success(await fixture.CallAsync(
            "email_draft",
            ("to", ToolResults.Of("client@example.com")),
            ("subject", "Draft quote"),
            ("text", "To be checked")));

        var action = Assert.Single(trial.Actions);
        Assert.Equal(SimulatedMailActionKind.Draft, action.Kind);
        Assert.Equal(["client@example.com"], action.Recipients);
        Assert.Equal("Draft quote", action.Subject);
        Assert.Contains("To be checked", action.Body, StringComparison.Ordinal);
        var found = ToolResults.Success(await fixture.CallAsync("email_search", ("folder", "drafts"), ("subject", "Draft quote")));
        Assert.Single(ToolResults.Objects(found, "messages"));
    }

    [Fact]
    [Trait("BR", "BR-02")]
    [Trait("BR", "BR-03")]
    public async Task ShouldMoveMarkDeleteAndRenameInTheTrialMailbox_AndRecordEachChange()
    {
        using var fixture = new ToolFixture(scoped: true);
        using var trial = EmailTrial.Enter(TrialMailboxSeed.Sample());
        var quote = ToolResults.Success(await fixture.CallAsync("email_search", ("subject", "Quote request")));
        var id = (string)Assert.Single(ToolResults.Objects(quote, "messages"))["id"]!;

        ToolResults.Success(await fixture.CallAsync("email_move", ("ids", ToolResults.Of(id)), ("destination", "archive")));
        var archived = ToolResults.Success(await fixture.CallAsync("email_search", ("folder", "archive"), ("subject", "Quote request")));
        Assert.Single(ToolResults.Objects(archived, "messages"));

        ToolResults.Success(await fixture.CallAsync("email_mark", ("ids", ToolResults.Of(id)), ("flagged", true)));
        ToolResults.Success(await fixture.CallAsync("email_create_folder", ("path", "Projects")));
        ToolResults.Success(await fixture.CallAsync("email_rename_folder", ("path", "Projects"), ("new_name", "Clients")));
        var folders = ToolResults.Success(await fixture.CallAsync("email_folders"));
        var paths = ToolResults.Objects(folders, "folders").Select(f => (string)f["path"]!).ToList();
        Assert.Contains("Clients", paths);
        Assert.DoesNotContain("Projects", paths);

        var movedId = trial.Actions[0].MessageId;
        ToolResults.Success(await fixture.CallAsync("email_delete", ("ids", ToolResults.Of(movedId))));
        var inTrash = ToolResults.Success(await fixture.CallAsync("email_search", ("folder", "trash"), ("subject", "Quote request")));
        Assert.Single(ToolResults.Objects(inTrash, "messages"));
        var inArchive = ToolResults.Success(await fixture.CallAsync("email_search", ("folder", "archive"), ("subject", "Quote request")));
        Assert.Empty(ToolResults.Objects(inArchive, "messages"));

        Assert.Equal(
            [
                SimulatedMailActionKind.Move,
                SimulatedMailActionKind.Mark,
                SimulatedMailActionKind.CreateFolder,
                SimulatedMailActionKind.RenameFolder,
                SimulatedMailActionKind.Delete,
            ],
            trial.Actions.Select(a => a.Kind));
        Assert.Equal("Archive", trial.Actions[0].Target);
        Assert.NotEmpty(trial.Actions[0].MessageId);
        Assert.Equal("Projects", trial.Actions[3].Folder);
        Assert.Equal("Clients", trial.Actions[3].Target);
        var mailbox = fixture.Mailbox();
        Assert.Empty(mailbox.Moves);
        Assert.Empty(mailbox.FlagUpdates);
        Assert.Empty(mailbox.Deletes);
        Assert.Empty(mailbox.Created);
        Assert.Empty(mailbox.Renamed);
    }

    [Fact]
    [Trait("BR", "BR-02")]
    public async Task ShouldRefuseToRenameARoleFolder_InATrial()
    {
        using var fixture = new ToolFixture(scoped: true);
        using var trial = EmailTrial.Enter(TrialMailboxSeed.Sample());

        var error = ToolResults.Failure(await fixture.CallAsync("email_rename_folder", ("path", "Sent"), ("new_name", "Outbox")));

        Assert.Contains("The sent folder is a system folder and cannot be renamed.", error, StringComparison.Ordinal);
        ToolResults.Success(await fixture.CallAsync(
            "email_send",
            ("to", ToolResults.Of("client@example.com")),
            ("subject", "After refusal"),
            ("text", "Still sendable")));
        var found = ToolResults.Success(await fixture.CallAsync("email_search", ("folder", "sent"), ("subject", "After refusal")));
        Assert.Single(ToolResults.Objects(found, "messages"));
        Assert.DoesNotContain(trial.Actions, a => a.Kind == SimulatedMailActionKind.RenameFolder);
    }

    [Fact]
    [Trait("BR", "BR-02")]
    public async Task ShouldRefuseANestedNewName_WhenRenamingAFolderInATrial()
    {
        using var fixture = new ToolFixture(scoped: true);
        using var trial = EmailTrial.Enter(TrialMailboxSeed.Sample());
        ToolResults.Success(await fixture.CallAsync("email_create_folder", ("path", "Projects")));

        var error = ToolResults.Failure(await fixture.CallAsync("email_rename_folder", ("path", "Projects"), ("new_name", "a/b")));

        Assert.Contains("`new_name` is a single folder name, without '/'.", error, StringComparison.Ordinal);
        Assert.DoesNotContain(trial.Actions, a => a.Kind == SimulatedMailActionKind.RenameFolder);
        ToolResults.Success(await fixture.CallAsync("email_rename_folder", ("path", "Projects"), ("new_name", "  Clients  ")));
        var folders = ToolResults.Success(await fixture.CallAsync("email_folders"));
        var paths = ToolResults.Objects(folders, "folders").Select(f => (string)f["path"]!).ToList();
        Assert.Contains("Clients", paths);
        Assert.DoesNotContain("Projects", paths);
    }

    [Fact]
    [Trait("BR", "BR-02")]
    [Trait("BR", "BR-03")]
    public async Task ShouldSaveTheTrialAttachmentBytesThroughTheVfs_AndRecordTheSave()
    {
        using var fixture = new ToolFixture(scoped: true);
        using var trial = EmailTrial.Enter(TrialMailboxSeed.Sample());
        var invoice = ToolResults.Success(await fixture.CallAsync("email_search", ("subject", "Invoice INV-2026-0912")));
        var id = (string)Assert.Single(ToolResults.Objects(invoice, "messages"))["id"]!;

        ToolResults.Success(await fixture.CallAsync("email_save_attachment", ("id", id), ("directory", "/output/att")));

        var text = await fixture.Files.TryReadAllTextAsync("/output/att/INV-2026-0912.txt", TestContext.Current.CancellationToken);
        Assert.NotNull(text);
        Assert.StartsWith("Invoice INV-2026-0912", text, StringComparison.Ordinal);
        var action = Assert.Single(trial.Actions);
        Assert.Equal(SimulatedMailActionKind.SaveAttachment, action.Kind);
        Assert.Equal(id, action.MessageId);
        Assert.Equal("/output/att/INV-2026-0912.txt", action.Target);
    }

    [Fact]
    [Trait("BR", "BR-02")]
    public async Task ShouldStartFromTheSeedAgain_WhenANewTrialIsEntered()
    {
        using var fixture = new ToolFixture(scoped: true);
        using (var first = EmailTrial.Enter(TrialMailboxSeed.Sample()))
        {
            var quote = ToolResults.Success(await fixture.CallAsync("email_search", ("subject", "Quote request")));
            var id = (string)Assert.Single(ToolResults.Objects(quote, "messages"))["id"]!;
            ToolResults.Success(await fixture.CallAsync("email_delete", ("ids", ToolResults.Of(id))));
            ToolResults.Success(await fixture.CallAsync(
                "email_send",
                ("to", ToolResults.Of("client@example.com")),
                ("subject", "Gone"),
                ("text", "Body")));
            Assert.NotEmpty(first.Actions);
        }

        using var second = EmailTrial.Enter(TrialMailboxSeed.Sample());
        var again = ToolResults.Success(await fixture.CallAsync("email_search", ("folder", "inbox"), ("subject", "Quote request")));

        Assert.Single(ToolResults.Objects(again, "messages"));
        Assert.Empty(second.Actions);
    }

    [Fact]
    [Trait("BR", "BR-02")]
    [Trait("BR", "BR-03")]
    public async Task ShouldRecordEveryAction_WhenToolsOfOneTrialRunConcurrently()
    {
        const int count = 400;
        using var fixture = new ToolFixture(scoped: true);
        using var trial = EmailTrial.Enter(TrialMailboxSeed.Sample());
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var calls = Enumerable.Range(0, count).Select(i => Task.Run(async () =>
        {
            await gate.Task;
            return await fixture.CallAsync(
                "email_draft",
                ("to", ToolResults.Of("client@example.com")),
                ("subject", $"Draft {i}"),
                ("text", "Body"));
        })).ToList();
        gate.SetResult();
        var results = await Task.WhenAll(calls);

        foreach (var result in results)
            ToolResults.Success(result);
        var actions = trial.Actions.ToList();
        Assert.Equal(count, actions.Count);
        Assert.All(actions, a => Assert.Equal(SimulatedMailActionKind.Draft, a.Kind));
        Assert.Equal(count, actions.Select(a => a.MessageId).Distinct().Count());
        Assert.Equal(count, actions.Select(a => a.Subject).Distinct().Count());
    }
}
