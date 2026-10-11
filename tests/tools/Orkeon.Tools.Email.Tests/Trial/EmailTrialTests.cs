using Orkeon.Tools.Email.Accounts;
using Orkeon.Tools.Email.Tests.Doubles;
using Orkeon.Tools.Email.Tests.Fixtures;
using Orkeon.Tools.Email.Trial;

namespace Orkeon.Tools.Email.Tests.Trial;

/// <summary>Inside a mail trial the tools read a fictitious mailbox and never reach the real provider.</summary>
public sealed class EmailTrialTests
{
    [Fact]
    [Trait("BR", "BR-02")]
    public async Task ShouldSearchTheTrialMailbox_WhenATrialIsEntered()
    {
        using var fixture = new ToolFixture(scoped: true);
        using var trial = EmailTrial.Enter(TrialMailboxSeed.Sample());

        var result = ToolResults.Success(await fixture.CallAsync("email_search", ("subject", "Quote request")));

        var message = Assert.Single(ToolResults.Objects(result, "messages"));
        Assert.Equal("Quote request: 40 office chairs", message["subject"]);
        Assert.Contains("laura.bennett@example.org", (string)message["from"]!, StringComparison.Ordinal);
        Assert.Empty(fixture.Mailbox().Searches);
    }

    [Fact]
    [Trait("BR", "BR-02")]
    public async Task ShouldRefuseARawQuery_InATrial()
    {
        using var fixture = new ToolFixture(scoped: true);
        using var trial = EmailTrial.Enter(TrialMailboxSeed.Sample());

        var error = ToolResults.Failure(await fixture.CallAsync("email_search", ("raw_query", "from:laura")));

        Assert.Contains("raw_query", error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "BR-04")]
    public async Task ShouldLoadTheSampleMailbox_WithItsFoldersAndMessages()
    {
        var messages = TrialMailboxSeed.Sample().Messages;

        Assert.Equal(12, messages.Count);
        Assert.Equal(
            ["Archive", "Drafts", "Inbox", "Sent", "Trash"],
            messages.Select(m => m.Folder).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        var attachment = Assert.Single(messages.SelectMany(m => m.Attachments), a => a.Name == "INV-2026-0912.txt");
        Assert.Equal("text/plain", attachment.MediaType);
        Assert.NotEmpty(attachment.Content);

        using var fixture = new ToolFixture(scoped: true);
        using var trial = EmailTrial.Enter(TrialMailboxSeed.Sample());
        var result = ToolResults.Success(await fixture.CallAsync("email_folders"));
        var folders = ToolResults.Objects(result, "folders");
        Assert.Equal(5, folders.Count);
        Assert.Equal(
            ["archive", "drafts", "inbox", "sent", "trash"],
            folders.Select(f => (string)f["role"]!).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["archive", "drafts", "inbox", "sent", "trash"],
            folders.Select(f => ((string)f["path"]!).ToLowerInvariant()).Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("BR", "BR-02")]
    public async Task ShouldNeverCallTheRealProvider_WhenATrialIsEntered()
    {
        using var fixture = new ToolFixture(scoped: true);
        using var trial = EmailTrial.Enter(TrialMailboxSeed.Sample());

        var search = ToolResults.Success(await fixture.CallAsync("email_search", ("subject", "Invoice")));
        var id = Assert.IsType<string>(Assert.Single(ToolResults.Objects(search, "messages"))["id"]);

        ToolResults.Success(await fixture.CallAsync("email_accounts"));
        ToolResults.Success(await fixture.CallAsync("email_folders"));
        ToolResults.Success(await fixture.CallAsync("email_read", ("id", id)));
        ToolResults.Success(await fixture.CallAsync("email_save_attachment", ("id", id), ("directory", "/output/att")));
        ToolResults.Success(await fixture.CallAsync("email_create_folder", ("path", "Projects")));
        ToolResults.Success(await fixture.CallAsync("email_rename_folder", ("path", "Projects"), ("new_name", "Clients")));
        ToolResults.Success(await fixture.CallAsync("email_move", ("ids", ToolResults.Of(id)), ("destination", "archive")));
        ToolResults.Success(await fixture.CallAsync("email_mark", ("ids", ToolResults.Of(id)), ("flagged", true)));
        ToolResults.Success(await fixture.CallAsync("email_draft", ("to", ToolResults.Of("client@example.com")), ("subject", "S"), ("text", "T")));
        ToolResults.Success(await fixture.CallAsync("email_send", ("to", ToolResults.Of("client@example.com")), ("subject", "S"), ("text", "T")));
        ToolResults.Success(await fixture.CallAsync("email_delete", ("ids", ToolResults.Of(id))));

        Assert.Empty(fixture.Mailboxes.MailboxRequests);
        Assert.Empty(fixture.Mailboxes.SenderRequests);
        Assert.Empty(fixture.Sender().Sent);
    }

    [Fact]
    [Trait("BR", "BR-02")]
    public void ShouldServeTheRealMailbox_WhenNoTrialIsEntered()
    {
        var fake = new FakeMailboxProvider();
        var account = ResolveFull();
        var scoped = new ScopedMailboxProvider(fake);

        Assert.Null(EmailTrial.Current);
        Assert.Same(fake.MailboxOf(account.Name), scoped.GetMailbox(account));
        Assert.Same(fake.SenderOf(account.Name), scoped.GetSender(account));
    }

    [Fact]
    [Trait("BR", "BR-02")]
    public async Task ShouldIsolateTwoTrials_WhenTheyRunConcurrently()
    {
        using var bothIn = new SemaphoreSlim(0, 2);
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<(string Own, string Other)> Flow(string own, string other)
        {
            using var fixture = new ToolFixture(scoped: true);
            var message = new TrialMailMessage { Folder = "Inbox", From = "a@example.org", Subject = own };
            using var trial = EmailTrial.Enter(TrialMailboxSeed.Sample().With([message]));
            bothIn.Release();
            await go.Task;
            var ownResult = ToolResults.Success(await fixture.CallAsync("email_search", ("subject", own)));
            var otherResult = ToolResults.Success(await fixture.CallAsync("email_search", ("subject", other)));
            return (
                string.Join("|", ToolResults.Objects(ownResult, "messages").Select(m => (string)m["subject"]!)),
                string.Join("|", ToolResults.Objects(otherResult, "messages").Select(m => (string)m["subject"]!)));
        }

        var a = Task.Run(() => Flow("Only in A", "Only in B"));
        var b = Task.Run(() => Flow("Only in B", "Only in A"));
        var bothEntered = Task.Run(
            async () =>
            {
                await bothIn.WaitAsync(TestContext.Current.CancellationToken);
                await bothIn.WaitAsync(TestContext.Current.CancellationToken);
            },
            TestContext.Current.CancellationToken);
        await Task.WhenAny(bothEntered, Task.WhenAll(a, b));
        go.TrySetResult();
        var resultA = await a;
        var resultB = await b;

        Assert.Equal(("Only in A", ""), resultA);
        Assert.Equal(("Only in B", ""), resultB);
        Assert.Null(EmailTrial.Current);
    }

    private static Orkeon.Tools.Email.Configuration.ResolvedEmailAccount ResolveFull()
    {
        using var fixture = new ToolFixture();
        var registry = new EmailAccountRegistry(Microsoft.Extensions.Options.Options.Create(fixture.Options));
        return registry.Resolve("full");
    }
}
