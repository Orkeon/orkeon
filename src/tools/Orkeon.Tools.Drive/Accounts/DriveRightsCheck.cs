using Orkeon.Tools.Drive.Administration;
using Orkeon.Tools.Drive.Configuration;

namespace Orkeon.Tools.Drive.Accounts;

/// <summary>Tells which drive rights a crew's tools need that no ready account grants.</summary>
public sealed class DriveRightsCheck
{
    private const string ShareTool = "drive_share";

    private readonly DriveAccountAdministration _administration;

    /// <summary>Creates the check over the accounts' administration.</summary>
    public DriveRightsCheck(DriveAccountAdministration administration) => _administration = administration;

    /// <summary>One line per drive tool whose right no ready account grants, over the accounts as they stand.</summary>
    public async Task<IReadOnlyList<string>> CheckAsync(IReadOnlyCollection<string> crewTools, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(crewTools);
        if (!crewTools.Any(tool => BaseRight(tool) != DriveRights.None))
            return [];

        var statuses = await _administration.ListAsync(cancellationToken).ConfigureAwait(false);
        return Check(crewTools, statuses);
    }

    /// <summary>One line per drive tool whose right no ready account grants.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Public instance API: the check is a registered service, its callers hold an instance.")]
    public IReadOnlyList<string> Check(IReadOnlyCollection<string> crewTools, IReadOnlyList<DriveAccountStatus> accounts)
    {
        ArgumentNullException.ThrowIfNull(crewTools);
        ArgumentNullException.ThrowIfNull(accounts);
        var lines = new List<string>();
        foreach (var tool in crewTools.Distinct(StringComparer.Ordinal))
        {
            var right = BaseRight(tool);
            if (right == DriveRights.None)
                continue;

            var needsAudience = tool == ShareTool;
            if (accounts.Any(account => Grants(account, right, needsAudience)))
                continue;

            lines.Add(needsAudience
                ? $"{tool} needs the {right} right with a non-empty Share:AllowedRecipients (or Share:AllowPublicLinks) on a ready drive account, and no account has it. Add it under Orkeon:Tools:Drive:Accounts:<name>:Rights and Share."
                : $"{tool} needs the {right} right on a ready drive account, and no account has it. Add it under Orkeon:Tools:Drive:Accounts:<name>:Rights.");
        }

        return lines;
    }

    /// <summary>The base right a drive tool needs.</summary>
    internal static DriveRights BaseRight(string tool) => tool switch
    {
        "drive_list" or "drive_search" or "drive_info" or "drive_read" or "drive_download" => DriveRights.Read,
        "drive_upload" or "drive_create_folder" => DriveRights.Write,
        "drive_move" => DriveRights.Organize,
        "drive_delete" => DriveRights.Delete,
        ShareTool => DriveRights.Share,
        _ => DriveRights.None,
    };

    private static bool Grants(DriveAccountStatus account, DriveRights right, bool needsAudience)
        => account.Ready
            && Enum.TryParse<DriveRights>(account.Rights, out var granted)
            && granted.HasFlag(right)
            && (!needsAudience || account.AllowedRecipients > 0 || account.AllowPublicLinks);
}
