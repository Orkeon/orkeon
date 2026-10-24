using System.Collections.Concurrent;
using Orkeon.Tools.Drive.Constants;

namespace Orkeon.Tools.Drive.Drives;

/// <summary>
/// The parents an engine has seen, and the climb from an item to the root folder that the
/// root check of both engines makes. An engine records every item it reads (<see cref="Remember"/>)
/// and says how to fetch an id it has not seen.
/// </summary>
/// <param name="fetchParent">
/// Reads an item from the provider: the id the provider gives it, which may differ from the one
/// asked with (an alias, another case), and its parent id, null for the root of the drive.
/// </param>
/// <param name="ids">How the provider compares ids: OneDrive's are case-insensitive.</param>
internal sealed class ParentChain(Func<string, CancellationToken, Task<(string Id, string? ParentId)>> fetchParent, StringComparer ids)
{
    // By the id an item was asked or seen with: the id the provider gives it, and its parent.
    private readonly ConcurrentDictionary<string, (string Id, string? ParentId)> _parents = new(ids);

    /// <summary>How the provider compares ids.</summary>
    public StringComparer Ids => ids;

    /// <summary>
    /// Records the parent of <paramref name="item"/> and returns it, for use in a projection. An
    /// item read without its parent's id (a OneDrive search hit) is not recorded: an unknown parent
    /// is not the root of the drive.
    /// </summary>
    public DriveItem Remember(DriveItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.ParentId is not null)
            _parents[item.Id] = (item.Id, item.ParentId);
        return item;
    }

    /// <summary>Forgets <paramref name="id"/>, deleted or moved out of reach.</summary>
    public void Forget(string id) => _parents.TryRemove(id, out _);

    /// <summary>
    /// Whether climbing the parents of <paramref name="item"/> reaches <paramref name="rootId"/>,
    /// within <see cref="DriveDefaults.MaxRootDepth"/> levels. A parent this account cannot open
    /// (another drive, a folder not shared with it) is not the root's subtree: the item is outside.
    /// </summary>
    public async Task<bool> ReachesAsync(DriveItem item, string rootId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (ids.Equals(item.Id, rootId))
            return true;

        var current = item.ParentId;
        for (var depth = 0; current is not null && depth < DriveDefaults.MaxRootDepth; depth++)
        {
            if (ids.Equals(current, rootId))
                return true;
            try
            {
                var (id, parent) = await ReadAsync(current, cancellationToken).ConfigureAwait(false);
                // A search result may name its parent by another id than the one the root was read with.
                if (ids.Equals(id, rootId))
                    return true;
                current = parent;
            }
            catch (DriveToolException ex) when (ex.Code == DriveErrorCode.ItemNotFound)
            {
                return false;
            }
        }

        return false;
    }

    private async Task<(string Id, string? ParentId)> ReadAsync(string id, CancellationToken cancellationToken)
    {
        if (_parents.TryGetValue(id, out var known))
            return known;
        var read = await fetchParent(id, cancellationToken).ConfigureAwait(false);
        _parents[id] = read;
        _parents[read.Id] = read;
        return read;
    }
}
