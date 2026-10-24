using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using Orkeon.Tools.Drive.Configuration;
using Orkeon.Tools.Drive.Constants;

namespace Orkeon.Tools.Drive.Drives.Graph;

/// <summary>
/// OneDrive, personal or for work, over Microsoft Graph in plain HTTP (<c>/me/drive</c>). The
/// root folder is addressed by path; a download goes through the pre-authenticated
/// <c>@microsoft.graph.downloadUrl</c>, never by following <c>/content</c>'s redirect with the
/// token; a large upload goes through an upload session, in chunks, without the token.
/// </summary>
internal sealed partial class OneDrive : IDrive
{
    /// <summary>The fields every item is read with.</summary>
    internal const string ItemFields = "id,name,size,file,folder,package,lastModifiedDateTime,parentReference,webUrl";

    private const string ConflictBehavior = "@microsoft.graph.conflictBehavior";
    private const string DownloadUrl = "@microsoft.graph.downloadUrl";
    private const string RootPathPrefix = "root:";

    private readonly DriveApiClient _api;
    private readonly ParentChain _parents;
    private DriveItem? _root;

    /// <summary>Creates the drive of the account <paramref name="api"/> speaks for.</summary>
    public OneDrive(DriveApiClient api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
        _parents = new ParentChain(FetchParentAsync, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public ResolvedDriveAccount Account => _api.Account;

    /// <inheritdoc />
    /// <remarks>
    /// <c>permanentDelete</c> exists on OneDrive for work and SharePoint only: a personal account
    /// (the <c>consumers</c> tenant) refuses it, so it is not offered there.
    /// </remarks>
    public DriveCapabilities Capabilities =>
        Account.OAuth.TokenEndpoint.AbsolutePath.Contains($"/{DriveDefaults.MicrosoftDefaultTenant}/", StringComparison.OrdinalIgnoreCase)
            ? DriveCapabilities.None
            : DriveCapabilities.PermanentDelete;

    /// <inheritdoc />
    public async Task<DriveItem> GetRootAsync(CancellationToken cancellationToken)
    {
        if (_root is { } cached)
            return cached;

        var address = Account.Root.Count == 0
            ? Graph($"me/drive/root?$select={ItemFields}")
            : Graph($"me/drive/root:/{string.Join('/', Account.Root.Select(Uri.EscapeDataString))}?$select={ItemFields}");
        DriveItem root;
        try
        {
            using var document = await _api.GetJsonAsync(address, cancellationToken).ConfigureAwait(false);
            root = ToItem(document.RootElement);
        }
        catch (DriveToolException ex) when (ex.Code == DriveErrorCode.ItemNotFound)
        {
            throw new DriveToolException(
                DriveErrorCode.InvalidConfiguration,
                $"The root folder {Account.RootPath} of drive account '{Account.Name}' does not exist in this OneDrive: create it, or fix {DriveDefaults.SectionName}:Accounts:{Account.Name}:Root.",
                ex);
        }

        if (!root.IsFolder)
            throw new DriveToolException(DriveErrorCode.InvalidConfiguration, $"The root {Account.RootPath} of drive account '{Account.Name}' is a file, not a folder.");
        _root = root;
        return root;
    }

    /// <inheritdoc />
    public async Task<DriveItem> GetItemAsync(string id, CancellationToken cancellationToken)
    {
        using var document = await _api.GetJsonAsync(Item(id, $"?$select={ItemFields}"), cancellationToken).ConfigureAwait(false);
        return _parents.Remember(ToItem(document.RootElement));
    }

    /// <inheritdoc />
    public async Task<bool> IsWithinAsync(DriveItem item, DriveItem root, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(root);
        if (_parents.Ids.Equals(item.Id, root.Id))
            return true;

        // An item shared from another drive is never inside, whatever its path.
        if (item.DriveId is { } drive && root.DriveId is { } rootDrive && !string.Equals(drive, rootDrive, StringComparison.OrdinalIgnoreCase))
            return false;

        // The path, when Graph gives it, answers at once. A search result may come without one,
        // or without the parent's id too (a parentReference holding the drive alone, on OneDrive
        // personal): the item is read again, then the climb by ids decides.
        if (UnderPath(item, root) is { } under)
            return under;

        if (item.ParentId is null)
        {
            try
            {
                item = await GetItemAsync(item.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (DriveToolException ex) when (ex.Code == DriveErrorCode.ItemNotFound)
            {
                return false;
            }

            if (UnderPath(item, root) is { } read)
                return read;
        }

        return await _parents.ReachesAsync(item, root.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<DrivePage> ListAsync(string folderId, int limit, string? cursor, CancellationToken cancellationToken)
    {
        var address = cursor is null
            ? Item(folderId, $"/children?$select={ItemFields}&$top={limit.ToString(CultureInfo.InvariantCulture)}&$orderby=name")
            : Cursor(cursor, limit);
        using var document = await _api.GetJsonAsync(address, cancellationToken).ConfigureAwait(false);
        var items = ReadValues(document.RootElement).OrderByDescending(item => item.IsFolder).ToList();
        return new DrivePage(items, NextLink(document.RootElement));
    }

    /// <inheritdoc />
    public async Task<DrivePage> SearchAsync(DriveSearchQuery query, int limit, string? cursor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!string.IsNullOrWhiteSpace(query.RawQuery))
            throw new DriveToolException(DriveErrorCode.Unsupported, "OneDrive has no query language: pass `text`, `name_contains` and the other criteria instead of `raw_query`.");

        var words = !string.IsNullOrWhiteSpace(query.Text) ? query.Text.Trim() : query.NameContains?.Trim();
        if (string.IsNullOrEmpty(words))
            throw new DriveToolException(DriveErrorCode.InvalidRequest, "OneDrive searches by words: pass `text` or `name_contains`.");

        Uri address;
        if (cursor is not null)
        {
            address = Cursor(cursor, limit);
        }
        else
        {
            // Searching from a folder covers its whole subtree, the account's root by default.
            var from = string.IsNullOrWhiteSpace(query.FolderId) ? (await GetRootAsync(cancellationToken).ConfigureAwait(false)).Id : query.FolderId.Trim();
            var literal = Uri.EscapeDataString(words.Replace("'", "''", StringComparison.Ordinal));
            address = Item(from, $"/search(q='{literal}')?$select={ItemFields}&$top={limit.ToString(CultureInfo.InvariantCulture)}");
        }

        using var document = await _api.GetJsonAsync(address, cancellationToken).ConfigureAwait(false);
        // Graph's search takes words only: the other criteria are applied to what it found.
        var items = ReadValues(document.RootElement).Where(item => Matches(item, query, words)).ToList();
        return new DrivePage(items, NextLink(document.RootElement));
    }

    /// <inheritdoc />
    public async Task<DriveContent> DownloadAsync(DriveItem item, string? exportFormat, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.IsFolder)
            throw new DriveToolException(DriveErrorCode.InvalidRequest, $"'{item.Name}' is a folder: there is nothing to download, list it instead.");
        if (!string.IsNullOrWhiteSpace(exportFormat))
            throw new DriveToolException(DriveErrorCode.InvalidRequest, $"'{item.Name}' is downloaded as it is: `format` only applies to Google Docs, Sheets, Slides and Drawings.");

        using var document = await _api.GetJsonAsync(Item(item.Id, $"?$select=id,{DownloadUrl}"), cancellationToken).ConfigureAwait(false);
        if (Text(document.RootElement, DownloadUrl) is not { } link || !Uri.TryCreate(link, UriKind.Absolute, out var address))
            throw new DriveToolException(DriveErrorCode.ServerError, $"Microsoft Graph gave no download address for '{item.Name}'.");

        var response = await _api.SendPreauthenticatedAsync(HttpMethod.Get, address, () => null, cancellationToken).ConfigureAwait(false);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return new DriveContent(stream, item.MimeType ?? "application/octet-stream", item.Name, response.Content.Headers.ContentLength, response);
    }

    /// <inheritdoc />
    public async Task<DriveItem> CreateFolderAsync(string parentId, string name, CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["name"] = name, ["folder"] = new JsonObject(), [ConflictBehavior] = "fail" };
        using var document = await _api.SendJsonAsync(HttpMethod.Post, Item(parentId, $"/children?$select={ItemFields}"), body, cancellationToken).ConfigureAwait(false);
        return _parents.Remember(ToItem(document.RootElement));
    }

    /// <inheritdoc />
    public async Task<DriveItem> UploadAsync(string parentId, string name, DriveUpload upload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(upload);
        var target = $"{Uri.EscapeDataString(parentId)}:/{Uri.EscapeDataString(name)}:";
        if (upload.Length <= DriveDefaults.OneDriveSimpleUploadBytes)
        {
            var response = await _api.SendAsync(
                HttpMethod.Put, Graph($"me/drive/items/{target}/content?{ConflictBehavior}=fail"), () => UploadContents.Whole(upload), cancellationToken).ConfigureAwait(false);
            using var document = await DriveApiClient.ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
            return _parents.Remember(ToItem(document.RootElement));
        }

        return await SessionUploadAsync(Graph($"me/drive/items/{target}/createUploadSession"), "fail", upload, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<DriveItem> ReplaceContentAsync(string id, DriveUpload upload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(upload);
        if (upload.Length <= DriveDefaults.OneDriveSimpleUploadBytes)
        {
            var response = await _api.SendAsync(HttpMethod.Put, Item(id, "/content"), () => UploadContents.Whole(upload), cancellationToken).ConfigureAwait(false);
            using var document = await DriveApiClient.ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
            return _parents.Remember(ToItem(document.RootElement));
        }

        return await SessionUploadAsync(Item(id, "/createUploadSession"), "replace", upload, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<DriveItem> MoveAsync(DriveItem item, string? newParentId, string? newName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        var body = new JsonObject { [ConflictBehavior] = "fail" };
        if (newParentId is not null)
            body["parentReference"] = new JsonObject { ["id"] = newParentId };
        if (newName is not null)
            body["name"] = newName;
        using var document = await _api.SendJsonAsync(HttpMethod.Patch, Item(item.Id, $"?$select={ItemFields}"), body, cancellationToken).ConfigureAwait(false);
        return _parents.Remember(ToItem(document.RootElement));
    }

    /// <inheritdoc />
    public async Task TrashAsync(string id, CancellationToken cancellationToken)
    {
        // Graph's DELETE moves the item to the recycle bin.
        using var _ = await _api.SendJsonAsync(HttpMethod.Delete, Item(id, string.Empty), body: null, cancellationToken).ConfigureAwait(false);
        _parents.Forget(id);
    }

    /// <inheritdoc />
    public async Task DeletePermanentlyAsync(string id, CancellationToken cancellationToken)
    {
        using var _ = await _api.SendJsonAsync(HttpMethod.Post, Item(id, "/permanentDelete"), body: null, cancellationToken).ConfigureAwait(false);
        _parents.Forget(id);
    }

    /// <inheritdoc />
    public async Task<DriveShareResult> ShareAsync(DriveItem item, DriveShareRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(request);
        if (request.PublicLink)
        {
            var link = new JsonObject { ["type"] = request.CanEdit ? "edit" : "view", ["scope"] = "anonymous" };
            using var created = await _api.SendJsonAsync(HttpMethod.Post, Item(item.Id, "/createLink"), link, cancellationToken).ConfigureAwait(false);
            var address = created.RootElement.TryGetProperty("link", out var value) ? Text(value, "webUrl") : null;
            return new DriveShareResult(address ?? item.WebUrl, []);
        }

        var invite = new JsonObject
        {
            ["recipients"] = new JsonArray(request.Recipients.Select(recipient => (JsonNode)new JsonObject { ["email"] = recipient }).ToArray()),
            ["roles"] = new JsonArray(request.CanEdit ? "write" : "read"),
            ["requireSignIn"] = true,
            ["sendInvitation"] = request.Notify,
        };
        if (request.Notify && !string.IsNullOrWhiteSpace(request.Message))
            invite["message"] = request.Message;
        using var _ = await _api.SendJsonAsync(HttpMethod.Post, Item(item.Id, "/invite"), invite, cancellationToken).ConfigureAwait(false);
        return new DriveShareResult(item.WebUrl, request.Recipients);
    }

    /// <summary>Reads one Graph <c>driveItem</c>.</summary>
    internal static DriveItem ToItem(JsonElement item)
    {
        var isFolder = item.TryGetProperty("folder", out _) || item.TryGetProperty("package", out _);
        string? mimeType = null;
        if (item.TryGetProperty("file", out var file))
            mimeType = Text(file, "mimeType");

        string? parentId = null, parentPath = null, driveId = null;
        if (item.TryGetProperty("parentReference", out var parent) && parent.ValueKind == JsonValueKind.Object)
        {
            parentId = Text(parent, "id");
            parentPath = NormalizePath(Text(parent, "path"));
            driveId = Text(parent, "driveId");
        }

        return new DriveItem
        {
            Id = Text(item, "id") ?? throw new DriveToolException(DriveErrorCode.ServerError, "Microsoft Graph answered an item without an id."),
            Name = Text(item, "name") ?? string.Empty,
            IsFolder = isFolder,
            MimeType = mimeType,
            Size = isFolder ? null : item.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) ? bytes : null,
            Modified = Text(item, "lastModifiedDateTime") is { } modified
                && DateTimeOffset.TryParse(modified, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when) ? when : null,
            ParentId = parentId,
            ParentPath = parentPath,
            DriveId = driveId,
            WebUrl = Text(item, "webUrl"),
        };
    }

    /// <summary>
    /// A Graph item path (<c>/drive/root:/Orkeon/agents</c>, <c>/drives/b!x/root:/…</c>) as a path
    /// from the root of the drive (<c>/Orkeon/agents</c>, <c>/</c> for the root itself), unescaped.
    /// </summary>
    internal static string? NormalizePath(string? path)
    {
        if (path is null)
            return null;
        var cut = path.IndexOf(RootPathPrefix, StringComparison.OrdinalIgnoreCase);
        if (cut < 0)
            return null;
        var rest = Uri.UnescapeDataString(path[(cut + RootPathPrefix.Length)..]).TrimEnd('/');
        return rest.Length == 0 ? "/" : rest;
    }

    /// <summary>Whether the path of <paramref name="item"/> lies under <paramref name="root"/>; null when either has no path.</summary>
    private static bool? UnderPath(DriveItem item, DriveItem root) =>
        item.ParentPath is { } parentPath && FullPath(root) is { } rootPath
            ? string.Equals(parentPath, rootPath, StringComparison.OrdinalIgnoreCase) || parentPath.StartsWith(rootPath + "/", StringComparison.OrdinalIgnoreCase)
            : null;

    private static string? FullPath(DriveItem folder) =>
        folder.ParentPath is null ? null : folder.ParentPath == "/" ? "/" + folder.Name : $"{folder.ParentPath}/{folder.Name}";

    private static bool Matches(DriveItem item, DriveSearchQuery query, string words)
    {
        if (!string.IsNullOrWhiteSpace(query.NameContains) && words != query.NameContains.Trim()
            && !item.Name.Contains(query.NameContains.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (query.Folders is { } folders && item.IsFolder != folders)
            return false;
        if (query.ModifiedAfter is { } after && (item.Modified is null || item.Modified <= after))
            return false;
        if (!string.IsNullOrWhiteSpace(query.MimeType))
        {
            var type = query.MimeType.Trim();
            var matches = item.MimeType is not null && (type.EndsWith('/')
                ? item.MimeType.StartsWith(type, StringComparison.OrdinalIgnoreCase)
                : string.Equals(item.MimeType, type, StringComparison.OrdinalIgnoreCase));
            if (!matches)
                return false;
        }

        return true;
    }

    private async Task<DriveItem> SessionUploadAsync(Uri create, string conflictBehavior, DriveUpload upload, CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["item"] = new JsonObject { [ConflictBehavior] = conflictBehavior } };
        Uri session;
        using (var opened = await _api.SendJsonAsync(HttpMethod.Post, create, body, cancellationToken).ConfigureAwait(false))
        {
            if (Text(opened.RootElement, "uploadUrl") is not { } url || !Uri.TryCreate(url, UriKind.Absolute, out var parsed))
                throw new DriveToolException(DriveErrorCode.ServerError, "Microsoft Graph opened no upload session (no uploadUrl).");
            session = parsed;
        }

        if (upload.Content.CanSeek)
            upload.Content.Position = 0;
        long sent = 0;
        while (true)
        {
            var chunk = await UploadContents.ReadChunkAsync(upload, DriveDefaults.OneDriveChunkBytes, cancellationToken).ConfigureAwait(false);
            if (chunk.Length == 0)
                throw new DriveToolException(DriveErrorCode.InvalidRequest, "The file ended before its announced length: it changed while it was being uploaded.");

            var first = sent;
            var last = sent + chunk.Length - 1;
            // The session address authorizes the chunks by itself: the token is not sent to it.
            var response = await _api.SendPreauthenticatedAsync(HttpMethod.Put, session, () =>
            {
                var content = new ByteArrayContent(chunk);
                content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(first, last, upload.Length);
                return content;
            }, cancellationToken, acceptAnyStatus: true).ConfigureAwait(false);
            sent += chunk.Length;

            if (response.StatusCode == HttpStatusCode.Accepted)
            {
                response.Dispose();
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                using var failure = await DriveApiClient.ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
                var (code, message) = DriveApiClient.ReadError(failure.RootElement.GetRawText());
                throw new DriveToolException(
                    status == 409 ? DriveErrorCode.Conflict : DriveErrorCode.ServerError,
                    string.Create(CultureInfo.InvariantCulture, $"Microsoft Graph stopped the upload at byte {first} with HTTP {status} ({code}: {message})."));
            }

            using var document = await DriveApiClient.ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
            return _parents.Remember(ToItem(document.RootElement));
        }
    }

    private async Task<(string Id, string? ParentId)> FetchParentAsync(string id, CancellationToken cancellationToken)
    {
        using var document = await _api.GetJsonAsync(Item(id, "?$select=id,parentReference"), cancellationToken).ConfigureAwait(false);
        var parent = document.RootElement.TryGetProperty("parentReference", out var reference) ? Text(reference, "id") : null;
        return (Text(document.RootElement, "id") ?? id, parent);
    }

    private Uri Cursor(string cursor, int limit)
    {
        // A cursor is the nextLink Graph handed out: it must still point at Graph, so that the
        // token never follows a forged one elsewhere.
        if (!Uri.TryCreate(cursor, UriKind.Absolute, out var link) || !_api.IsApiAddress(link))
            throw new DriveToolException(DriveErrorCode.InvalidRequest, "This cursor was not handed out by drive_list or drive_search: start the listing again without it.");

        // The link carries the page size it was asked with; the page asked for now may be smaller.
        var top = $"top={limit.ToString(CultureInfo.InvariantCulture)}";
        var rewritten = TopParameter().Replace(link.OriginalString, match => match.Groups[1].Value + top, 1);
        return new Uri(rewritten);
    }

    [GeneratedRegex(@"(\$|%24)top=\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TopParameter();

    private List<DriveItem> ReadValues(JsonElement root) =>
        root.TryGetProperty("value", out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Select(ToItem).Select(_parents.Remember).ToList()
            : [];

    private static string? NextLink(JsonElement root) => Text(root, "@odata.nextLink");

    private static Uri Graph(string relative) => new(DriveDefaults.GraphBaseUri, relative);

    private static Uri Item(string id, string suffix) => Graph($"me/drive/items/{Uri.EscapeDataString(id)}{suffix}");

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
