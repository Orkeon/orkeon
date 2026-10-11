using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Security;

namespace Orkeon.Tools.Email.Trial;

/// <summary>A mail trial entered for the current async flow.</summary>
public sealed class EmailTrial : IDisposable
{
    private static readonly AsyncLocal<EmailTrial?> Flow = new();

    /// <summary>The fictitious account offered inside a trial when none is configured: every right, nobody barred, no cap.</summary>
    internal static readonly ResolvedEmailAccount Account = new()
    {
        Name = "trial",
        Provider = EmailProvider.Custom,
        Address = "trial@example.invalid",
        Rights = EmailRights.Read | EmailRights.Organize | EmailRights.Draft | EmailRights.Send | EmailRights.Delete | EmailRights.Purge,
        Incoming = IncomingProtocol.Imap,
        Outgoing = OutgoingProtocol.Smtp,
        Auth = new ResolvedAuth { Method = EmailAuthMethod.Password, Username = "trial" },
        Send = new SendPolicy(["*"], null, null),
        SaveSentCopy = false,
    };

    private readonly EmailTrial? _previous;
    private readonly List<SimulatedMailAction> _actions = [];

    private EmailTrial(TrialMailboxSeed seed, EmailTrial? previous)
    {
        Mailbox = new TrialMailbox(seed, Append);
        _previous = previous;
    }

    /// <summary>The trial of the current flow, or null.</summary>
    public static EmailTrial? Current => Flow.Value;

    /// <summary>The actions simulated so far.</summary>
    public IReadOnlyList<SimulatedMailAction> Actions
    {
        get
        {
            lock (_actions)
                return [.. _actions];
        }
    }

    internal TrialMailbox Mailbox { get; }

    /// <summary>The hourly send count of this trial, apart from the real one.</summary>
    internal SendQuota Quota { get; } = new(TimeProvider.System);

    /// <summary>Records that an attachment of <paramref name="messageId"/> was saved at <paramref name="path"/>.</summary>
    internal void RecordAttachmentSaved(string account, string messageId, string path) =>
        Append(new SimulatedMailAction { Kind = SimulatedMailActionKind.SaveAttachment, Account = account, MessageId = messageId, Target = path });

    private void Append(SimulatedMailAction action)
    {
        lock (_actions)
            _actions.Add(action);
    }

    /// <summary>The trial mailbox as the account <paramref name="account"/> sees it.</summary>
    internal TrialMailbox MailboxFor(string account) => Mailbox.ForAccount(account);

    /// <summary>Enters a trial over <paramref name="seed"/>.</summary>
    public static EmailTrial Enter(TrialMailboxSeed seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        var trial = new EmailTrial(seed, Flow.Value);
        Flow.Value = trial;
        return trial;
    }

    /// <inheritdoc />
    public void Dispose() => Flow.Value = _previous;
}
