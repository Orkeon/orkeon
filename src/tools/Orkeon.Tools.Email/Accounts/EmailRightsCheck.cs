using Orkeon.Tools.Email.Administration;
using Orkeon.Tools.Email.Configuration;

namespace Orkeon.Tools.Email.Accounts;

/// <summary>Tells which mail rights a crew's tools need that no ready account grants.</summary>
public sealed class EmailRightsCheck
{
    private const string SendTool = "email_send";

    private readonly EmailAccountAdministration _administration;

    /// <summary>Creates the check over the accounts' administration.</summary>
    public EmailRightsCheck(EmailAccountAdministration administration) => _administration = administration;

    /// <summary>One line per mail tool whose right no ready account grants, over the accounts as they stand.</summary>
    public async Task<IReadOnlyList<string>> CheckAsync(IReadOnlyCollection<string> crewTools, CancellationToken cancellationToken)
    {
        if (!crewTools.Any(tool => BaseRight(tool) != EmailRights.None))
        {
            return [];
        }

        var statuses = await _administration.ListAsync(cancellationToken).ConfigureAwait(false);
        return Check(crewTools, statuses);
    }

    /// <summary>One line per mail tool whose right no ready account grants.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Public instance API: the check is a registered service, its callers hold an instance.")]
    public IReadOnlyList<string> Check(IReadOnlyCollection<string> crewTools, IReadOnlyList<EmailAccountStatus> accounts)
    {
        var lines = new List<string>();
        foreach (var tool in crewTools.Distinct(StringComparer.Ordinal))
        {
            var right = BaseRight(tool);
            if (right == EmailRights.None)
            {
                continue;
            }

            var needsRecipients = tool == SendTool;
            if (accounts.Any(a => Grants(a, right, needsRecipients)))
            {
                continue;
            }

            lines.Add(needsRecipients
                ? $"{tool} needs the {right} right with a non-empty Send:AllowedRecipients on a ready e-mail account, and no account has it. Add it in Studio (Settings › E-mail) or under Orkeon:Tools:Email:Accounts:<name>:Rights and Send:AllowedRecipients."
                : $"{tool} needs the {right} right on a ready e-mail account, and no account has it. Add it in Studio (Settings › E-mail) or under Orkeon:Tools:Email:Accounts:<name>:Rights.");
        }

        return lines;
    }

    /// <summary>The base right a mail tool needs.</summary>
    internal static EmailRights BaseRight(string tool) => tool switch
    {
        "email_folders" or "email_search" or "email_read" or "email_save_attachment" => EmailRights.Read,
        "email_create_folder" or "email_rename_folder" or "email_move" or "email_mark" => EmailRights.Organize,
        "email_delete" => EmailRights.Delete,
        "email_draft" => EmailRights.Draft,
        SendTool => EmailRights.Send,
        _ => EmailRights.None,
    };

    private static bool Grants(EmailAccountStatus account, EmailRights right, bool needsRecipients)
        => account.Ready
            && Enum.TryParse<EmailRights>(account.Rights, out var granted)
            && granted.HasFlag(right)
            && (!needsRecipients || account.AllowedRecipients > 0);
}
