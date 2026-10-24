using System.Text;
using Orkeon.Tools.Drive.Configuration;
using Orkeon.Tools.Drive.Drives;

namespace Orkeon.Tools.Drive.Tests.Doubles;

/// <summary>
/// In-memory <see cref="IDrive"/>: a tree of items under a root, their bytes, and the calls made,
/// so a tool test drives the gate and the tool without any HTTP. Items are added with
/// <see cref="AddFolder"/> and <see cref="AddFile"/>; anything outside the root sits under
/// <see cref="OutsideId"/>.
/// </summary>
internal sealed class FakeDrive : IDrive
{
    /// <summary>The id of the account's root folder.</summary>
    public const string RootId = "root-id";

    /// <summary>The id of a folder that lies outside the root.</summary>
    public const string OutsideId = "outside-id";

    private readonly Dictionary<string, DriveItem> _items = new(StringComparer.Ordinal);

    /// <summary>Creates the drive of <paramref name="account"/>.</summary>
    public FakeDrive(ResolvedDriveAccount account)
    {
        Account = account;
        _items[RootId] = new DriveItem { Id = RootId, Name = "agents", IsFolder = true };
        _items[OutsideId] = new DriveItem { Id = OutsideId, Name = "private", IsFolder = true };
    }

    /// <inheritdoc />
    public ResolvedDriveAccount Account { get; }

    /// <inheritdoc />
    public DriveCapabilities Capabilities { get; set; } = DriveCapabilities.PermanentDelete;

    /// <summary>The bytes of each file, by id.</summary>
    public Dictionary<string, byte[]> Contents { get; } = new(StringComparer.Ordinal);

    /// <summary>Every call made, one line each (<c>Upload root-id report.txt</c>).</summary>
    public List<string> Calls { get; } = [];

    /// <summary>The shares made.</summary>
    public List<(string Id, DriveShareRequest Request)> Shares { get; } = [];

    /// <summary>The page size each listing was asked with.</summary>
    public List<int> ListLimits { get; } = [];

    /// <summary>The page size and cursor each search was asked with.</summary>
    public List<(int Limit, string? Cursor)> Searches { get; } = [];

    /// <summary>Adds a folder under <paramref name="parentId"/>.</summary>
    public DriveItem AddFolder(string id, string name, string parentId = RootId) =>
        Add(new DriveItem { Id = id, Name = name, IsFolder = true, ParentId = parentId });

    /// <summary>Adds a file under <paramref name="parentId"/>.</summary>
    public DriveItem AddFile(string id, string name, string content, string mimeType = "text/plain", string parentId = RootId, bool googleDocument = false)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        Contents[id] = bytes;
        return Add(new DriveItem
        {
            Id = id,
            Name = name,
            IsFolder = false,
            MimeType = mimeType,
            Size = googleDocument ? null : bytes.Length,
            ParentId = parentId,
            IsGoogleDocument = googleDocument,
            WebUrl = $"https://drive.example/{id}",
        });
    }

    /// <summary>The item <paramref name="id"/>, or null.</summary>
    public DriveItem? Find(string id) => _items.GetValueOrDefault(id);

    /// <inheritdoc />
    public Task<DriveItem> GetRootAsync(CancellationToken cancellationToken) => Task.FromResult(_items[RootId]);

    /// <inheritdoc />
    public Task<DriveItem> GetItemAsync(string id, CancellationToken cancellationToken) =>
        _items.TryGetValue(id, out var item)
            ? Task.FromResult(item)
            : throw new DriveToolException(DriveErrorCode.ItemNotFound, $"Fake drive: no item {id}.");

    /// <inheritdoc />
    public Task<bool> IsWithinAsync(DriveItem item, DriveItem root, CancellationToken cancellationToken)
    {
        for (DriveItem? current = item; current is not null; current = current.ParentId is { } parent ? _items.GetValueOrDefault(parent) : null)
        {
            if (current.Id == root.Id)
                return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<DrivePage> ListAsync(string folderId, int limit, string? cursor, CancellationToken cancellationToken)
    {
        ListLimits.Add(limit);
        // A cursor written "folder:<id>" lists that folder instead, as a forged OneDrive cursor
        // (an address of another folder) would.
        if (cursor is not null && cursor.StartsWith("folder:", StringComparison.Ordinal))
        {
            folderId = cursor["folder:".Length..];
            cursor = null;
        }

        var children = _items.Values.Where(item => item.ParentId == folderId).OrderByDescending(item => item.IsFolder).ThenBy(item => item.Name, StringComparer.Ordinal).ToList();
        var start = cursor is null ? 0 : int.Parse(cursor, System.Globalization.CultureInfo.InvariantCulture);
        var page = children.Skip(start).Take(limit).ToList();
        var next = start + page.Count < children.Count ? (start + page.Count).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        return Task.FromResult(new DrivePage(page, next));
    }

    /// <inheritdoc />
    public Task<DrivePage> SearchAsync(DriveSearchQuery query, int limit, string? cursor, CancellationToken cancellationToken)
    {
        // The whole drive, as Google searches it: the tool sifts out what lies outside the root.
        Calls.Add($"Search {query.NameContains ?? query.Text}");
        Searches.Add((limit, cursor));
        var found = _items.Values
            .Where(item => item.Id is not RootId and not OutsideId)
            .Where(item => query.NameContains is null || item.Name.Contains(query.NameContains, StringComparison.OrdinalIgnoreCase))
            .Where(item => query.Folders is null || item.IsFolder == query.Folders)
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ToList();
        var start = cursor is null ? 0 : int.Parse(cursor, System.Globalization.CultureInfo.InvariantCulture);
        var page = found.Skip(start).Take(limit).ToList();
        var next = start + page.Count < found.Count ? (start + page.Count).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        return Task.FromResult(new DrivePage(page, next));
    }

    /// <inheritdoc />
    public Task<DriveContent> DownloadAsync(DriveItem item, string? exportFormat, CancellationToken cancellationToken)
    {
        Calls.Add($"Download {item.Id} {exportFormat}".TrimEnd());
        var name = item.IsGoogleDocument ? $"{item.Name}.{exportFormat ?? "txt"}" : item.Name;
        return Task.FromResult(new DriveContent(new MemoryStream(Contents[item.Id]), item.MimeType ?? "application/octet-stream", name, Contents[item.Id].Length));
    }

    /// <inheritdoc />
    public Task<DriveItem> CreateFolderAsync(string parentId, string name, CancellationToken cancellationToken)
    {
        Calls.Add($"CreateFolder {parentId} {name}");
        return Task.FromResult(AddFolder($"folder-{_items.Count}", name, parentId));
    }

    /// <inheritdoc />
    public async Task<DriveItem> UploadAsync(string parentId, string name, DriveUpload upload, CancellationToken cancellationToken)
    {
        Calls.Add($"Upload {parentId} {name} {upload.MimeType} {upload.Length}");
        var bytes = await ReadAsync(upload, cancellationToken);
        return AddFile($"file-{_items.Count}", name, Encoding.UTF8.GetString(bytes), upload.MimeType, parentId);
    }

    /// <inheritdoc />
    public async Task<DriveItem> ReplaceContentAsync(string id, DriveUpload upload, CancellationToken cancellationToken)
    {
        Calls.Add($"Replace {id} {upload.MimeType}");
        Contents[id] = await ReadAsync(upload, cancellationToken);
        return _items[id];
    }

    /// <inheritdoc />
    public Task<DriveItem> MoveAsync(DriveItem item, string? newParentId, string? newName, CancellationToken cancellationToken)
    {
        Calls.Add($"Move {item.Id} {newParentId} {newName}".TrimEnd());
        return Task.FromResult(Add(item with { ParentId = newParentId ?? item.ParentId, Name = newName ?? item.Name }));
    }

    /// <inheritdoc />
    public Task TrashAsync(string id, CancellationToken cancellationToken)
    {
        Calls.Add($"Trash {id}");
        _items.Remove(id);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeletePermanentlyAsync(string id, CancellationToken cancellationToken)
    {
        Calls.Add($"Purge {id}");
        _items.Remove(id);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<DriveShareResult> ShareAsync(DriveItem item, DriveShareRequest request, CancellationToken cancellationToken)
    {
        Shares.Add((item.Id, request));
        return Task.FromResult(new DriveShareResult(request.PublicLink ? "https://drive.example/public/" + item.Id : item.WebUrl, request.Recipients));
    }

    private DriveItem Add(DriveItem item)
    {
        _items[item.Id] = item;
        return item;
    }

    private static async Task<byte[]> ReadAsync(DriveUpload upload, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await upload.Content.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }
}

/// <summary>Hands out one <see cref="FakeDrive"/> per account.</summary>
internal sealed class FakeDriveFactory : IDriveFactory
{
    private readonly Dictionary<string, FakeDrive> _drives = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The drive of <paramref name="account"/>, created on first use.</summary>
    public IDrive GetDrive(ResolvedDriveAccount account) => DriveOf(account);

    /// <summary>The fake drive of <paramref name="account"/>.</summary>
    public FakeDrive DriveOf(ResolvedDriveAccount account)
    {
        if (!_drives.TryGetValue(account.Name, out var drive))
            _drives[account.Name] = drive = new FakeDrive(account);
        return drive;
    }
}
