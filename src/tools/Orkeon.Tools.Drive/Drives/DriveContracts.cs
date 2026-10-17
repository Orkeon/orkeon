using Orkeon.Tools.Drive.Configuration;

namespace Orkeon.Tools.Drive.Drives;

/// <summary>One file or folder, the same shape whatever the provider.</summary>
internal sealed record DriveItem
{
    /// <summary>The provider's id: opaque, stable across moves and renames.</summary>
    public required string Id { get; init; }

    /// <summary>The name, extension included.</summary>
    public required string Name { get; init; }

    /// <summary>Whether it is a folder.</summary>
    public required bool IsFolder { get; init; }

    /// <summary>The MIME type, when the provider says.</summary>
    public string? MimeType { get; init; }

    /// <summary>The size in bytes; null for a folder or a Google document, which have none.</summary>
    public long? Size { get; init; }

    /// <summary>Last modification.</summary>
    public DateTimeOffset? Modified { get; init; }

    /// <summary>The id of the folder holding it; null for the root of the drive.</summary>
    public string? ParentId { get; init; }

    /// <summary>
    /// The path of the folder holding it, from the root of the drive (<c>/Orkeon/agents</c>), when
    /// the provider says (OneDrive does; Google Drive does not).
    /// </summary>
    public string? ParentPath { get; init; }

    /// <summary>The address a person opens it at in a browser.</summary>
    public string? WebUrl { get; init; }

    /// <summary>Whether it is a Google document (Docs, Sheets, Slides…), read and downloaded by export.</summary>
    public bool IsGoogleDocument { get; init; }

    /// <summary>Whether it sits in the trash.</summary>
    public bool Trashed { get; init; }
}

/// <summary>A page of items and the cursor of the next one.</summary>
/// <param name="Items">The items.</param>
/// <param name="Cursor">Opaque cursor of the next page; null on the last.</param>
internal sealed record DrivePage(IReadOnlyList<DriveItem> Items, string? Cursor);

/// <summary>What a search looks for. Every criterion narrows; none of them is required but one.</summary>
internal sealed record DriveSearchQuery
{
    /// <summary>Words looked for in names and, where the provider indexes it, content.</summary>
    public string? Text { get; init; }

    /// <summary>Text the name must contain.</summary>
    public string? NameContains { get; init; }

    /// <summary>MIME type the items must have, or a prefix ending with <c>/</c> (<c>image/</c>).</summary>
    public string? MimeType { get; init; }

    /// <summary>Folders only (true), files only (false), or both (null).</summary>
    public bool? Folders { get; init; }

    /// <summary>Items modified after this instant.</summary>
    public DateTimeOffset? ModifiedAfter { get; init; }

    /// <summary>The folder to search in (its whole subtree on OneDrive, its direct children on Google Drive); null for the account's root.</summary>
    public string? FolderId { get; init; }

    /// <summary>A native query: Google Drive's <c>q</c> language; refused on OneDrive.</summary>
    public string? RawQuery { get; init; }
}

/// <summary>The bytes of a file, or of a Google document exported to a format.</summary>
internal sealed class DriveContent : IAsyncDisposable
{
    private readonly IDisposable? _owner;

    /// <summary>Wraps <paramref name="stream"/>; <paramref name="owner"/> (the HTTP response) is disposed with it.</summary>
    public DriveContent(Stream stream, string mimeType, string fileName, long? length, IDisposable? owner = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Stream = stream;
        MimeType = mimeType;
        FileName = fileName;
        Length = length;
        _owner = owner;
    }

    /// <summary>The bytes.</summary>
    public Stream Stream { get; }

    /// <summary>Their MIME type.</summary>
    public string MimeType { get; }

    /// <summary>The name a saved copy takes (an exported document gets the extension of its format).</summary>
    public string FileName { get; }

    /// <summary>The length, when the provider announced it.</summary>
    public long? Length { get; }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Stream.DisposeAsync().ConfigureAwait(false);
        _owner?.Dispose();
    }
}

/// <summary>A file to upload: its bytes, read once, and what the provider is told about them.</summary>
/// <param name="Content">The bytes, read from the start.</param>
/// <param name="Length">How many bytes <paramref name="Content"/> holds.</param>
/// <param name="MimeType">Their MIME type.</param>
internal sealed record DriveUpload(Stream Content, long Length, string MimeType);

/// <summary>Who a share is for and what they may do.</summary>
internal sealed record DriveShareRequest
{
    /// <summary>The addresses invited; empty for a link.</summary>
    public IReadOnlyList<string> Recipients { get; init; } = [];

    /// <summary>Whether the recipients (or the link) may edit, not only read.</summary>
    public bool CanEdit { get; init; }

    /// <summary>Whether to create a link anyone holding it can open, instead of inviting recipients.</summary>
    public bool PublicLink { get; init; }

    /// <summary>A message for the invitation, when the provider sends one.</summary>
    public string? Message { get; init; }

    /// <summary>Whether the provider e-mails the recipients.</summary>
    public bool Notify { get; init; }
}

/// <summary>What a share produced.</summary>
/// <param name="Link">The address to open the item at: the link created, or the item's own address.</param>
/// <param name="Recipients">The addresses that now have access.</param>
internal sealed record DriveShareResult(string? Link, IReadOnlyList<string> Recipients);

/// <summary>What a provider can do beyond the common ground.</summary>
[Flags]
internal enum DriveCapabilities
{
    /// <summary>The common ground only.</summary>
    None = 0,

    /// <summary>Deletes for good, bypassing the trash.</summary>
    PermanentDelete = 1,
}

/// <summary>
/// One account's drive, the port both engines implement. Ids are the provider's own; the
/// confinement to the account's root folder is the gate's job (<c>DriveAccess</c>), which asks
/// the engine whether an item descends from it.
/// </summary>
internal interface IDrive
{
    /// <summary>The account this drive belongs to.</summary>
    ResolvedDriveAccount Account { get; }

    /// <summary>What the provider can do beyond the common ground.</summary>
    DriveCapabilities Capabilities { get; }

    /// <summary>The folder the account is confined to: its <c>Root</c>, else the root of the drive.</summary>
    Task<DriveItem> GetRootAsync(CancellationToken cancellationToken);

    /// <summary>The item <paramref name="id"/>.</summary>
    Task<DriveItem> GetItemAsync(string id, CancellationToken cancellationToken);

    /// <summary>Whether <paramref name="item"/> is <paramref name="root"/> or lies beneath it.</summary>
    Task<bool> IsWithinAsync(DriveItem item, DriveItem root, CancellationToken cancellationToken);

    /// <summary>A page of the children of folder <paramref name="folderId"/>, folders first, then by name.</summary>
    Task<DrivePage> ListAsync(string folderId, int limit, string? cursor, CancellationToken cancellationToken);

    /// <summary>A page of the items matching <paramref name="query"/>.</summary>
    Task<DrivePage> SearchAsync(DriveSearchQuery query, int limit, string? cursor, CancellationToken cancellationToken);

    /// <summary>
    /// The bytes of <paramref name="item"/>; a Google document is exported to
    /// <paramref name="exportFormat"/> (an extension such as <c>pdf</c>), or to its default format.
    /// </summary>
    Task<DriveContent> DownloadAsync(DriveItem item, string? exportFormat, CancellationToken cancellationToken);

    /// <summary>Creates folder <paramref name="name"/> in <paramref name="parentId"/>; refuses a name already taken.</summary>
    Task<DriveItem> CreateFolderAsync(string parentId, string name, CancellationToken cancellationToken);

    /// <summary>Creates file <paramref name="name"/> in <paramref name="parentId"/>; refuses a name already taken.</summary>
    Task<DriveItem> UploadAsync(string parentId, string name, DriveUpload upload, CancellationToken cancellationToken);

    /// <summary>Replaces the content of file <paramref name="id"/>, keeping its id, name and place.</summary>
    Task<DriveItem> ReplaceContentAsync(string id, DriveUpload upload, CancellationToken cancellationToken);

    /// <summary>Moves <paramref name="item"/> to <paramref name="newParentId"/> and/or renames it to <paramref name="newName"/>.</summary>
    Task<DriveItem> MoveAsync(DriveItem item, string? newParentId, string? newName, CancellationToken cancellationToken);

    /// <summary>Moves <paramref name="id"/> to the trash.</summary>
    Task TrashAsync(string id, CancellationToken cancellationToken);

    /// <summary>Deletes <paramref name="id"/> for good; only with <see cref="DriveCapabilities.PermanentDelete"/>.</summary>
    Task DeletePermanentlyAsync(string id, CancellationToken cancellationToken);

    /// <summary>Shares <paramref name="item"/>.</summary>
    Task<DriveShareResult> ShareAsync(DriveItem item, DriveShareRequest request, CancellationToken cancellationToken);
}

/// <summary>Opens the drive of an account.</summary>
internal interface IDriveFactory
{
    /// <summary>The drive of <paramref name="account"/>; the same instance for the same account.</summary>
    IDrive GetDrive(ResolvedDriveAccount account);
}
