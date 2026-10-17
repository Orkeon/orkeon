using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Tools.Drive.Configuration;
using Orkeon.Tools.Drive.Constants;

namespace Orkeon.Tools.Drive.Drives.Google;

/// <summary>
/// Google Drive ("My Drive") over the Drive API v3, in plain HTTP. Folders are found by name
/// under their parent, since the API has no paths; Google's own documents are read and
/// downloaded by export; uploads go through a resumable session, sent in one request.
/// </summary>
internal sealed class GoogleDrive : IDrive
{
    /// <summary>The fields every item is read with.</summary>
    internal const string ItemFields = "id,name,mimeType,size,modifiedTime,parents,webViewLink,trashed";

    private const string ListFields = "nextPageToken,files(" + ItemFields + ")";

    private readonly DriveApiClient _api;
    private readonly ConcurrentDictionary<string, string?> _parents = new(StringComparer.Ordinal);
    private DriveItem? _root;

    /// <summary>Creates the drive of the account <paramref name="api"/> speaks for.</summary>
    public GoogleDrive(DriveApiClient api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    /// <inheritdoc />
    public ResolvedDriveAccount Account => _api.Account;

    /// <inheritdoc />
    public DriveCapabilities Capabilities => DriveCapabilities.PermanentDelete;

    /// <inheritdoc />
    public async Task<DriveItem> GetRootAsync(CancellationToken cancellationToken)
    {
        if (_root is { } cached)
            return cached;

        using var top = await _api.GetJsonAsync(FileAddress("root", $"fields={ItemFields}"), cancellationToken).ConfigureAwait(false);
        var folder = ToItem(top.RootElement);
        foreach (var segment in Account.Root)
        {
            var query = $"'{Escape(folder.Id)}' in parents and name = '{Escape(segment)}' and mimeType = '{DriveDefaults.GoogleFolderMimeType}' and trashed = false";
            using var found = await _api.GetJsonAsync(FilesAddress($"q={Uri.EscapeDataString(query)}&pageSize=2&fields={Uri.EscapeDataString(ListFields)}"), cancellationToken).ConfigureAwait(false);
            folder = ReadFiles(found.RootElement).FirstOrDefault()
                ?? throw new DriveToolException(
                    DriveErrorCode.InvalidConfiguration,
                    $"The root folder {Account.RootPath} of drive account '{Account.Name}' does not exist in this Google Drive ('{segment}' is missing): create it, or fix {DriveDefaults.SectionName}:Accounts:{Account.Name}:Root.");
        }

        _root = folder;
        return folder;
    }

    /// <inheritdoc />
    public async Task<DriveItem> GetItemAsync(string id, CancellationToken cancellationToken)
    {
        using var document = await _api.GetJsonAsync(FileAddress(id, $"fields={ItemFields}"), cancellationToken).ConfigureAwait(false);
        var item = ToItem(document.RootElement);
        _parents[item.Id] = item.ParentId;
        return item;
    }

    /// <inheritdoc />
    public async Task<bool> IsWithinAsync(DriveItem item, DriveItem root, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(root);
        if (item.Id == root.Id)
            return true;
        // The whole drive: everything the account can open is in reach, shared items included.
        if (Account.Root.Count == 0)
            return true;

        var current = item.ParentId;
        for (var depth = 0; current is not null && depth < DriveDefaults.MaxRootDepth; depth++)
        {
            if (current == root.Id)
                return true;
            current = await ParentOfAsync(current, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    /// <inheritdoc />
    public async Task<DrivePage> ListAsync(string folderId, int limit, string? cursor, CancellationToken cancellationToken)
    {
        var query = $"'{Escape(folderId)}' in parents and trashed = false";
        return await PageAsync(query, "folder,name", limit, cursor, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<DrivePage> SearchAsync(DriveSearchQuery query, int limit, string? cursor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var clauses = new List<string> { "trashed = false" };
        if (!string.IsNullOrWhiteSpace(query.Text))
            clauses.Add($"fullText contains '{Escape(query.Text.Trim())}'");
        if (!string.IsNullOrWhiteSpace(query.NameContains))
            clauses.Add($"name contains '{Escape(query.NameContains.Trim())}'");
        if (!string.IsNullOrWhiteSpace(query.MimeType))
        {
            var type = query.MimeType.Trim();
            clauses.Add(type.EndsWith('/') ? $"mimeType contains '{Escape(type)}'" : $"mimeType = '{Escape(type)}'");
        }

        if (query.Folders is { } folders)
            clauses.Add($"mimeType {(folders ? "=" : "!=")} '{DriveDefaults.GoogleFolderMimeType}'");
        if (query.ModifiedAfter is { } after)
            clauses.Add($"modifiedTime > '{after.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)}'");
        if (!string.IsNullOrWhiteSpace(query.FolderId))
            clauses.Add($"'{Escape(query.FolderId.Trim())}' in parents");
        if (!string.IsNullOrWhiteSpace(query.RawQuery))
            clauses.Add($"({query.RawQuery.Trim()})");

        // fullText search ranks by relevance and refuses an order; the others list by recency.
        var orderBy = string.IsNullOrWhiteSpace(query.Text) ? "modifiedTime desc" : null;
        return await PageAsync(string.Join(" and ", clauses), orderBy, limit, cursor, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<DriveContent> DownloadAsync(DriveItem item, string? exportFormat, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.IsFolder)
            throw new DriveToolException(DriveErrorCode.InvalidRequest, $"'{item.Name}' is a folder: there is nothing to download, list it instead.");

        if (item.IsGoogleDocument)
        {
            var (extension, mimeType) = GoogleExports.Resolve(item, exportFormat);
            var exported = await _api.SendAsync(
                HttpMethod.Get, FileAddress(item.Id, "export", $"mimeType={Uri.EscapeDataString(mimeType)}"), () => null, cancellationToken).ConfigureAwait(false);
            return await ContentOfAsync(exported, mimeType, $"{GoogleExports.StripExtension(item.Name)}.{extension}", cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(exportFormat))
        {
            throw new DriveToolException(
                DriveErrorCode.InvalidRequest,
                $"'{item.Name}' is a stored file, downloaded as it is: `format` only applies to Google Docs, Sheets, Slides and Drawings.");
        }

        var response = await _api.SendAsync(HttpMethod.Get, FileAddress(item.Id, "alt=media"), () => null, cancellationToken).ConfigureAwait(false);
        return await ContentOfAsync(response, item.MimeType ?? "application/octet-stream", item.Name, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<DriveItem> CreateFolderAsync(string parentId, string name, CancellationToken cancellationToken)
    {
        await RefuseTakenNameAsync(parentId, name, cancellationToken).ConfigureAwait(false);
        var body = new JsonObject
        {
            ["name"] = name,
            ["mimeType"] = DriveDefaults.GoogleFolderMimeType,
            ["parents"] = new JsonArray(parentId),
        };
        using var document = await _api.SendJsonAsync(HttpMethod.Post, FilesAddress($"fields={ItemFields}"), body, cancellationToken).ConfigureAwait(false);
        return Remember(ToItem(document.RootElement));
    }

    /// <inheritdoc />
    public async Task<DriveItem> UploadAsync(string parentId, string name, DriveUpload upload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(upload);
        await RefuseTakenNameAsync(parentId, name, cancellationToken).ConfigureAwait(false);
        var metadata = new JsonObject { ["name"] = name, ["parents"] = new JsonArray(parentId) };
        var session = await OpenSessionAsync(HttpMethod.Post, UploadAddress("files", $"uploadType=resumable&fields={ItemFields}"), metadata, upload, cancellationToken).ConfigureAwait(false);
        return await SendSessionAsync(session, upload, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<DriveItem> ReplaceContentAsync(string id, DriveUpload upload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(upload);
        var session = await OpenSessionAsync(
            HttpMethod.Patch, UploadAddress($"files/{Uri.EscapeDataString(id)}", $"uploadType=resumable&fields={ItemFields}"), new JsonObject(), upload, cancellationToken).ConfigureAwait(false);
        return await SendSessionAsync(session, upload, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<DriveItem> MoveAsync(DriveItem item, string? newParentId, string? newName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        var destination = newParentId ?? item.ParentId;
        if (destination is not null && (newParentId is not null || newName is not null))
            await RefuseTakenNameAsync(destination, newName ?? item.Name, cancellationToken, except: item.Id).ConfigureAwait(false);

        var query = $"fields={ItemFields}";
        if (newParentId is not null && newParentId != item.ParentId)
        {
            query += $"&addParents={Uri.EscapeDataString(newParentId)}";
            if (item.ParentId is not null)
                query += $"&removeParents={Uri.EscapeDataString(item.ParentId)}";
        }

        var body = new JsonObject();
        if (newName is not null)
            body["name"] = newName;
        using var document = await _api.SendJsonAsync(HttpMethod.Patch, FileAddress(item.Id, query), body, cancellationToken).ConfigureAwait(false);
        return Remember(ToItem(document.RootElement));
    }

    /// <inheritdoc />
    public async Task TrashAsync(string id, CancellationToken cancellationToken)
    {
        using var _ = await _api.SendJsonAsync(HttpMethod.Patch, FileAddress(id, "fields=id"), new JsonObject { ["trashed"] = true }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeletePermanentlyAsync(string id, CancellationToken cancellationToken)
    {
        using var _ = await _api.SendJsonAsync(HttpMethod.Delete, FileAddress(id, query: null), body: null, cancellationToken).ConfigureAwait(false);
        _parents.TryRemove(id, out var _);
    }

    /// <inheritdoc />
    public async Task<DriveShareResult> ShareAsync(DriveItem item, DriveShareRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(request);
        var role = request.CanEdit ? "writer" : "reader";
        if (request.PublicLink)
        {
            using var _ = await _api.SendJsonAsync(
                HttpMethod.Post, FileAddress(item.Id, "permissions", "fields=id"), new JsonObject { ["type"] = "anyone", ["role"] = role }, cancellationToken).ConfigureAwait(false);
            return new DriveShareResult(item.WebUrl, []);
        }

        foreach (var recipient in request.Recipients)
        {
            var query = $"fields=id&sendNotificationEmail={(request.Notify ? "true" : "false")}";
            if (request.Notify && !string.IsNullOrWhiteSpace(request.Message))
                query += $"&emailMessage={Uri.EscapeDataString(request.Message)}";
            using var _ = await _api.SendJsonAsync(
                HttpMethod.Post,
                FileAddress(item.Id, "permissions", query),
                new JsonObject { ["type"] = "user", ["role"] = role, ["emailAddress"] = recipient },
                cancellationToken).ConfigureAwait(false);
        }

        return new DriveShareResult(item.WebUrl, request.Recipients);
    }

    /// <summary>Reads one Drive API file resource.</summary>
    internal static DriveItem ToItem(JsonElement file)
    {
        var mimeType = Text(file, "mimeType");
        var parents = file.TryGetProperty("parents", out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0
            ? list[0].GetString()
            : null;
        return new DriveItem
        {
            Id = Text(file, "id") ?? throw new DriveToolException(DriveErrorCode.ServerError, "Google Drive answered an item without an id."),
            Name = Text(file, "name") ?? string.Empty,
            IsFolder = mimeType == DriveDefaults.GoogleFolderMimeType,
            MimeType = mimeType,
            Size = Text(file, "size") is { } size && long.TryParse(size, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes) ? bytes : null,
            Modified = Text(file, "modifiedTime") is { } modified && DateTimeOffset.TryParse(modified, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when) ? when : null,
            ParentId = parents,
            WebUrl = Text(file, "webViewLink"),
            IsGoogleDocument = mimeType is not null
                && mimeType.StartsWith(DriveDefaults.GoogleNativePrefix, StringComparison.Ordinal)
                && mimeType != DriveDefaults.GoogleFolderMimeType,
            Trashed = file.TryGetProperty("trashed", out var trashed) && trashed.ValueKind == JsonValueKind.True,
        };
    }

    /// <summary>A literal of the Drive query language: backslashes and quotes escaped.</summary>
    internal static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);

    private async Task<DrivePage> PageAsync(string query, string? orderBy, int limit, string? cursor, CancellationToken cancellationToken)
    {
        var parameters = $"q={Uri.EscapeDataString(query)}&pageSize={limit.ToString(CultureInfo.InvariantCulture)}&fields={Uri.EscapeDataString(ListFields)}";
        if (orderBy is not null)
            parameters += $"&orderBy={Uri.EscapeDataString(orderBy)}";
        if (!string.IsNullOrEmpty(cursor))
            parameters += $"&pageToken={Uri.EscapeDataString(cursor)}";

        using var document = await _api.GetJsonAsync(FilesAddress(parameters), cancellationToken).ConfigureAwait(false);
        var items = ReadFiles(document.RootElement).Select(Remember).ToList();
        return new DrivePage(items, Text(document.RootElement, "nextPageToken"));
    }

    private async Task<string?> ParentOfAsync(string id, CancellationToken cancellationToken)
    {
        if (_parents.TryGetValue(id, out var known))
            return known;
        using var document = await _api.GetJsonAsync(FileAddress(id, "fields=id,parents"), cancellationToken).ConfigureAwait(false);
        var parent = document.RootElement.TryGetProperty("parents", out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0
            ? list[0].GetString()
            : null;
        _parents[id] = parent;
        return parent;
    }

    private async Task RefuseTakenNameAsync(string parentId, string name, CancellationToken cancellationToken, string? except = null)
    {
        // Drive lets two items share a name in a folder; an agent addressing files by name would
        // then pick the wrong one, so the family refuses it like a file system would.
        var query = $"'{Escape(parentId)}' in parents and name = '{Escape(name)}' and trashed = false";
        using var document = await _api.GetJsonAsync(FilesAddress($"q={Uri.EscapeDataString(query)}&pageSize=2&fields={Uri.EscapeDataString(ListFields)}"), cancellationToken).ConfigureAwait(false);
        // The item being moved or renamed does not take its own name.
        if (ReadFiles(document.RootElement).FirstOrDefault(found => found.Id != except) is { } taken)
            throw new DriveToolException(DriveErrorCode.Conflict, $"An item named '{name}' is already in that folder (id {taken.Id}): choose another name, or replace its content with drive_upload and `replace_id`.");
    }

    private async Task<Uri> OpenSessionAsync(HttpMethod method, Uri address, JsonObject metadata, DriveUpload upload, CancellationToken cancellationToken)
    {
        var json = metadata.ToJsonString();
        using var response = await _api.SendAsync(
            method,
            address,
            () => new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            cancellationToken,
            headers =>
            {
                headers.TryAddWithoutValidation("X-Upload-Content-Type", upload.MimeType);
                headers.TryAddWithoutValidation("X-Upload-Content-Length", upload.Length.ToString(CultureInfo.InvariantCulture));
            }).ConfigureAwait(false);
        var session = response.Headers.Location;
        if (session is null || !_api.IsApiAddress(session))
            throw new DriveToolException(DriveErrorCode.ServerError, "Google Drive opened no upload session (no Location header on its own host).");
        return session;
    }

    private async Task<DriveItem> SendSessionAsync(Uri session, DriveUpload upload, CancellationToken cancellationToken)
    {
        // The session address authorizes the upload by itself: the token is not sent again.
        var response = await _api.SendPreauthenticatedAsync(HttpMethod.Put, session, () => UploadContents.Whole(upload), cancellationToken).ConfigureAwait(false);
        using var document = await DriveApiClient.ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        return Remember(ToItem(document.RootElement));
    }

    private static async Task<DriveContent> ContentOfAsync(HttpResponseMessage response, string mimeType, string fileName, CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return new DriveContent(stream, mimeType, fileName, response.Content.Headers.ContentLength, response);
    }

    private DriveItem Remember(DriveItem item)
    {
        _parents[item.Id] = item.ParentId;
        return item;
    }

    private static List<DriveItem> ReadFiles(JsonElement root) =>
        root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array
            ? files.EnumerateArray().Select(ToItem).ToList()
            : [];

    private static Uri FilesAddress(string query) => new(DriveDefaults.GoogleApiBase, $"files?{query}");

    private static Uri FileAddress(string id, string? query) =>
        new(DriveDefaults.GoogleApiBase, query is null ? $"files/{Uri.EscapeDataString(id)}" : $"files/{Uri.EscapeDataString(id)}?{query}");

    private static Uri FileAddress(string id, string subResource, string query) =>
        new(DriveDefaults.GoogleApiBase, $"files/{Uri.EscapeDataString(id)}/{subResource}?{query}");

    private static Uri UploadAddress(string path, string query) => new(DriveDefaults.GoogleUploadBase, $"{path}?{query}");

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
