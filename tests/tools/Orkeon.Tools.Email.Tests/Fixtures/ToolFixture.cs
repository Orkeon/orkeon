using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Email.Accounts;
using Orkeon.Tools.Email.Administration;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Security;
using Orkeon.Tools.Email.Tests.Doubles;
using Orkeon.Tools.Email.Tools;

namespace Orkeon.Tools.Email.Tests.Fixtures;

/// <summary>
/// The thirteen e-mail tools over fake mailboxes and senders, a rights-aware virtual file system
/// (<c>/workspace</c> read-only, <c>/output</c> writable) and a fake clock, with these accounts:
/// <list type="bullet">
/// <item><c>full</c> (default): every right, sends to <c>*@example.com</c> and <c>boss@corp.test</c>, at most 3 recipients and 2 messages an hour;</item>
/// <item><c>reader</c>: Read only;</item>
/// <item><c>organizer</c>: Organize only;</item>
/// <item><c>drafter</c>: Draft only, no allow-list;</item>
/// <item><c>closed</c>: Read, Draft and Send, but an empty allow-list;</item>
/// <item><c>nocopy</c>: Read and Send to anyone, no copy filed in Sent.</item>
/// </list>
/// </summary>
internal sealed class ToolFixture : IDisposable
{
    private readonly Dictionary<string, ToolBase> _tools = new(StringComparer.Ordinal);

    /// <summary>Creates the fixture.</summary>
    public ToolFixture(bool withholdRejected = false)
    {
        Options.DefaultAccount = "full";
        Options.Screening.WithholdRejected = withholdRejected;
        var full = TestAccounts.Custom(TestAccounts.AllRights);
        full.Send.AllowedRecipients.AddRange(["*@example.com", "boss@corp.test"]);
        full.Send.MaxRecipients = 3;
        full.Send.MaxPerHour = 2;
        full.DisplayName = "Agent";
        Options.Accounts["full"] = full;
        Options.Accounts["reader"] = TestAccounts.Custom(EmailRights.Read);
        Options.Accounts["organizer"] = TestAccounts.Custom(EmailRights.Organize);
        Options.Accounts["drafter"] = TestAccounts.Custom(EmailRights.Draft);
        Options.Accounts["closed"] = TestAccounts.Custom(EmailRights.Read | EmailRights.Draft | EmailRights.Send);
        var noCopy = TestAccounts.Custom(EmailRights.Read | EmailRights.Send);
        noCopy.SaveSentCopy = false;
        noCopy.Send.AllowedRecipients.Add("*");
        Options.Accounts["nocopy"] = noCopy;

        var options = Microsoft.Extensions.Options.Options.Create(Options);
        var registry = new EmailAccountRegistry(options);
        var access = new EmailAccess(registry, Mailboxes);
        var screen = new EmailContentScreen(options);
        Administration = new EmailAccountAdministration(registry, Credentials.Provider, Credentials.OAuth, Mailboxes);

        Add(new EmailAccountsTool(Administration));
        Add(new EmailFoldersTool(access));
        Add(new EmailSearchTool(access, screen));
        Add(new EmailReadTool(access, screen));
        Add(new EmailSaveAttachmentTool(access, Files));
        Add(new EmailCreateFolderTool(access));
        Add(new EmailRenameFolderTool(access));
        Add(new EmailMoveTool(access));
        Add(new EmailMarkTool(access));
        Add(new EmailDeleteTool(access));
        Add(new EmailDraftTool(access, Files));
        Add(new EmailSendTool(access, Files, new SendQuota(Credentials.Time), Credentials.Time));
        Add(new EmailParserTool(Files, screen));
    }

    /// <summary>The bound options, before any account resolves.</summary>
    public EmailToolsOptions Options { get; } = new();

    /// <summary>Mailboxes and senders by account name.</summary>
    public FakeMailboxProvider Mailboxes { get; } = new();

    /// <summary>Clock, token store and environment (the test password is set).</summary>
    public CredentialsFixture Credentials { get; } = new();

    /// <summary>The virtual file system.</summary>
    public FakeRightsFileSystemService Files { get; } =
        new FakeRightsFileSystemService().AddMount("/workspace", FileAccessRights.ReadOnly).AddMount("/output", FileAccessRights.ReadWrite);

    /// <summary>The administration behind <c>email_accounts</c>.</summary>
    public EmailAccountAdministration Administration { get; }

    /// <summary>The tools by name.</summary>
    public IReadOnlyDictionary<string, ToolBase> Tools => _tools;

    /// <summary>The mailbox of <paramref name="account"/>.</summary>
    public FakeMailbox Mailbox(string account = "full") => Mailboxes.MailboxOf(account);

    /// <summary>The sender of <paramref name="account"/>.</summary>
    public FakeMailSender Sender(string account = "full") => Mailboxes.SenderOf(account);

    /// <summary>Calls <paramref name="tool"/> with <paramref name="parameters"/>.</summary>
    public Task<ToolCallResponse> CallAsync(string tool, params (string Key, object? Value)[] parameters) =>
        _tools[tool].CallAsync(ToolResults.Call(tool, parameters), TestContext.Current.CancellationToken);

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var tool in _tools.Values)
            tool.Dispose();
        Credentials.Dispose();
    }

    private void Add(ToolBase tool) => _tools.Add(tool.Name, tool);
}
