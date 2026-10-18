using System.Collections.Concurrent;
using Orkeon.Tools.Drive.Constants;

namespace Orkeon.Tools.Drive.Drives;

/// <summary>
/// The parents an engine has seen, and the climb from an item to the root folder that the
/// root check of both engines makes. An engine records every item it reads (<see cref="Remember"/>)
/// and says how to fetch the parent of an id it has not seen.
/// </summary>
/// <param name="fetchParent">Reads the parent id of an item from the provider; null for the root of the drive.</param>
internal sealed class ParentChain(Func<string, CancellationToken, Task<string?>> fetchParent)
{
    private readonly ConcurrentDictionary<string, string?> _parents = new(StringComparer.Ordinal);

    /// <summary>Records the parent of <paramref name="item"/> and returns it, for use in a projection.</summary>
    public DriveItem Remember(DriveItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _parents[item.Id] = item.ParentId;
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
        if (item.Id == rootId)
            return true;

        var current = item.ParentId;
        for (var depth = 0; current is not null && depth < DriveDefaults.MaxRootDepth; depth++)
        {
            if (current == rootId)
                return true;
            try
            {
                current = await ParentOfAsync(current, cancellationToken).ConfigureAwait(false);
            }
            catch (DriveToolException ex) when (ex.Code == DriveErrorCode.ItemNotFound)
            {
                return false;
            }
        }

        return false;
    }

    private async Task<string?> ParentOfAsync(string id, CancellationToken cancellationToken)
    {
        if (_parents.TryGetValue(id, out var known))
            return known;
        var parent = await fetchParent(id, cancellationToken).ConfigureAwait(false);
        _parents[id] = parent;
        return parent;
    }
}
