using Orkeon.Tools.Drive.Configuration;
using Orkeon.Tools.Drive.Drives;

namespace Orkeon.Tools.Drive.Accounts;

/// <summary>
/// The gate every tool goes through: it resolves the account, checks that the account grants
/// the right the operation needs, and hands out items only from inside the account's root
/// folder. Refusals say which right is missing and where an operator adds it; an item outside
/// the root is reported as not found, so the gate never confirms what lies beyond it.
/// </summary>
internal sealed class DriveAccess
{
    /// <summary>The id an agent passes for the account's root folder.</summary>
    public const string RootAlias = "root";

    private readonly IDriveAccountRegistry _accounts;
    private readonly IDriveFactory _drives;

    /// <summary>Creates the gate.</summary>
    public DriveAccess(IDriveAccountRegistry accounts, IDriveFactory drives)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(drives);
        _accounts = accounts;
        _drives = drives;
    }

    /// <summary>The registry behind the gate.</summary>
    public IDriveAccountRegistry Accounts => _accounts;

    /// <summary>The account named <paramref name="account"/>, checked for <paramref name="right"/>.</summary>
    public ResolvedDriveAccount Authorize(string? account, DriveRights right)
    {
        var resolved = _accounts.Resolve(account);
        Demand(resolved, right);
        return resolved;
    }

    /// <summary>The drive of an authorized account.</summary>
    public IDrive Drive(ResolvedDriveAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return _drives.GetDrive(account);
    }

    /// <summary>
    /// The item <paramref name="id"/> — the account's root folder for an empty id or
    /// <see cref="RootAlias"/> — once it is known to lie inside that root.
    /// </summary>
    public static async Task<DriveItem> ItemAsync(IDrive drive, string? id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(drive);
        var root = await drive.GetRootAsync(cancellationToken).ConfigureAwait(false);
        var trimmed = id?.Trim();
        if (string.IsNullOrEmpty(trimmed) || string.Equals(trimmed, RootAlias, StringComparison.OrdinalIgnoreCase) || trimmed == root.Id)
            return root;

        var item = await drive.GetItemAsync(trimmed, cancellationToken).ConfigureAwait(false);
        // The whole drive: everything the account can open is in reach, shared items included.
        if (drive.Account.Root.Count > 0 && !await drive.IsWithinAsync(item, root, cancellationToken).ConfigureAwait(false))
            throw NotFound(drive.Account, trimmed);
        return item;
    }

    /// <summary>The folder <paramref name="id"/>, inside the account's root; a file is refused.</summary>
    public static async Task<DriveItem> FolderAsync(IDrive drive, string? id, CancellationToken cancellationToken)
    {
        var item = await ItemAsync(drive, id, cancellationToken).ConfigureAwait(false);
        if (!item.IsFolder)
            throw new DriveToolException(DriveErrorCode.InvalidRequest, $"'{item.Name}' ({item.Id}) is a file, not a folder.");
        return item;
    }

    /// <summary>
    /// The items of <paramref name="items"/> that lie inside the account's root folder, in order:
    /// a search cannot be bounded to a subtree (Google Drive) or may not be (OneDrive personal), so
    /// its results are sifted here. The root itself is left out.
    /// </summary>
    public static async Task<IReadOnlyList<DriveItem>> WithinRootAsync(IDrive drive, IReadOnlyList<DriveItem> items, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(drive);
        ArgumentNullException.ThrowIfNull(items);
        if (drive.Account.Root.Count == 0)
            return items;

        var root = await drive.GetRootAsync(cancellationToken).ConfigureAwait(false);
        var kept = new List<DriveItem>(items.Count);
        foreach (var item in items)
        {
            if (item.Id != root.Id && await drive.IsWithinAsync(item, root, cancellationToken).ConfigureAwait(false))
                kept.Add(item);
        }

        return kept;
    }

    /// <summary>The refusal of an item the account cannot see: missing, or outside its root folder.</summary>
    public static DriveToolException NotFound(ResolvedDriveAccount account, string id)
    {
        ArgumentNullException.ThrowIfNull(account);
        var where = account.Root.Count == 0 ? "in this drive" : $"under the account's root folder {account.RootPath}";
        return new DriveToolException(DriveErrorCode.ItemNotFound, $"No item '{id}' {where}: list or search again for its id.");
    }

    /// <summary>Throws unless <paramref name="account"/> grants <paramref name="right"/>.</summary>
    public static void Demand(ResolvedDriveAccount account, DriveRights right)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (right == DriveRights.None || account.Grants(right))
            return;

        // Only what is missing: "the Read, Write right" would name one the account has.
        var missing = right & ~account.Rights;
        var several = (missing & (missing - 1)) != 0;
        throw new DriveToolException(
            DriveErrorCode.RightDenied,
            $"Drive account '{account.Name}' does not grant the {missing} {(several ? "rights" : "right")} (it grants: {account.Rights}). " +
            $"An operator adds {(several ? "them" : "it")} under {Constants.DriveDefaults.SectionName}:Accounts:{account.Name}:Rights.");
    }
}
