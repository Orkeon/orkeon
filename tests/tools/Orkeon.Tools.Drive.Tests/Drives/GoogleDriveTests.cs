using System.Net;
using System.Text;
using Orkeon.Tools.Drive.Configuration;
using Orkeon.Tools.Drive.Drives;
using Orkeon.Tools.Drive.Tests.Fixtures;

namespace Orkeon.Tools.Drive.Tests.Drives;

/// <summary>The Google Drive engine against a scripted Drive API v3.</summary>
public sealed class GoogleDriveTests : IDisposable
{
    private const string Api = "https://www.googleapis.com/drive/v3/";
    private const string Folder = "application/vnd.google-apps.folder";

    private readonly DriveFixture _fixture = new(TestDriveAccounts.Resolve("perso", TestDriveAccounts.Google()));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Should_find_the_root_folder_by_name_under_each_parent_and_keep_it()
    {
        MapRoot();

        var root = await _fixture.Drive.GetRootAsync(Token);
        await _fixture.Drive.GetRootAsync(Token);

        Assert.Equal("agents-id", root.Id);
        Assert.True(root.IsFolder);
        var lookups = _fixture.Handler.RequestsTo(HttpMethod.Get, Api + "files?q=");
        Assert.Equal(2, lookups.Count);
        Assert.Contains("'my-drive' in parents and name = 'Orkeon' and mimeType = 'application/vnd.google-apps.folder' and trashed = false", lookups[0].DecodedUri, StringComparison.Ordinal);
        Assert.Contains("'orkeon-id' in parents and name = 'agents'", lookups[1].DecodedUri, StringComparison.Ordinal);
        Assert.All(_fixture.Handler.Requests, request => Assert.Equal(DriveFixture.Bearer, request.Authorization));
    }

    [Fact]
    public async Task Should_say_which_segment_of_the_root_is_missing()
    {
        _fixture.Handler.Map(HttpMethod.Get, Api + "files/root?", FakeHttpResponse.Json(File("my-drive", "My Drive", Folder)));
        _fixture.Handler.Map(HttpMethod.Get, Api + "files?q=", FakeHttpResponse.Json("""{"files":[]}"""));

        var error = await Assert.ThrowsAsync<DriveToolException>(() => _fixture.Drive.GetRootAsync(Token));

        Assert.Equal(DriveErrorCode.InvalidConfiguration, error.Code);
        Assert.Contains("'Orkeon' is missing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_tell_an_item_under_the_root_from_one_beside_it_by_climbing_its_parents()
    {
        MapRoot();
        _fixture.Handler.Map(HttpMethod.Get, Api + "files/sub-id?fields=id,parents", FakeHttpResponse.Json("""{"id":"sub-id","parents":["agents-id"]}"""));
        _fixture.Handler.Map(HttpMethod.Get, Api + "files/other-id?fields=id,parents", FakeHttpResponse.Json("""{"id":"other-id","parents":["orkeon-id"]}"""));
        _fixture.Handler.Map(HttpMethod.Get, Api + "files/orkeon-id?fields=id,parents", FakeHttpResponse.Json("""{"id":"orkeon-id","parents":["my-drive"]}"""));
        _fixture.Handler.Map(HttpMethod.Get, Api + "files/my-drive?fields=id,parents", FakeHttpResponse.Json("""{"id":"my-drive"}"""));
        var root = await _fixture.Drive.GetRootAsync(Token);

        Assert.True(await _fixture.Drive.IsWithinAsync(Item("deep", "sub-id"), root, Token));
        Assert.False(await _fixture.Drive.IsWithinAsync(Item("beside", "other-id"), root, Token));
        Assert.False(await _fixture.Drive.IsWithinAsync(Item("shared-with-me", parentId: null), root, Token));
    }

    [Fact]
    public async Task Should_list_a_folder_folders_first_and_hand_back_the_page_token()
    {
        _fixture.Handler.Map(HttpMethod.Get, Api + "files?q=", FakeHttpResponse.Json(
            $$"""{"nextPageToken":"page-2","files":[{{File("f1", "Plans", Folder)}},{{File("d1", "notes.txt", "text/plain", size: "12")}}]}"""));

        var page = await _fixture.Drive.ListAsync("folder-id", 5, "page-1", Token);

        Assert.Equal("page-2", page.Cursor);
        Assert.Equal(["Plans", "notes.txt"], page.Items.Select(item => item.Name));
        Assert.Equal(12, page.Items[1].Size);
        var request = Assert.Single(_fixture.Handler.Requests).DecodedUri;
        Assert.Contains("q='folder-id' in parents and trashed = false", request, StringComparison.Ordinal);
        Assert.Contains("pageSize=5", request, StringComparison.Ordinal);
        Assert.Contains("orderBy=folder,name", request, StringComparison.Ordinal);
        Assert.Contains("pageToken=page-1", request, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_build_one_query_from_every_criterion_and_escape_its_literals()
    {
        _fixture.Handler.Map(HttpMethod.Get, Api + "files?q=", FakeHttpResponse.Json("""{"files":[]}"""));

        await _fixture.Drive.SearchAsync(new DriveSearchQuery
        {
            NameContains = "O'Brien",
            MimeType = "image/",
            Folders = false,
            ModifiedAfter = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            FolderId = "f-1",
            RawQuery = "starred = true",
        }, 10, null, Token);

        var request = Assert.Single(_fixture.Handler.Requests).DecodedUri;
        Assert.Contains(
            "trashed = false and name contains 'O\\'Brien' and mimeType contains 'image/' and mimeType != 'application/vnd.google-apps.folder' " +
            "and modifiedTime > '2026-10-01T00:00:00Z' and 'f-1' in parents and (starred = true)",
            request, StringComparison.Ordinal);
        Assert.Contains("orderBy=modifiedTime desc", request, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_search_full_text_without_an_order()
    {
        _fixture.Handler.Map(HttpMethod.Get, Api + "files?q=", FakeHttpResponse.Json("""{"files":[]}"""));

        await _fixture.Drive.SearchAsync(new DriveSearchQuery { Text = "budget 2027" }, 10, null, Token);

        var request = Assert.Single(_fixture.Handler.Requests).DecodedUri;
        Assert.Contains("fullText contains 'budget 2027'", request, StringComparison.Ordinal);
        Assert.DoesNotContain("orderBy", request, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_download_a_stored_file_as_media()
    {
        _fixture.Handler.Map(HttpMethod.Get, Api + "files/d1?alt=media", new FakeHttpResponse(HttpStatusCode.OK, "hello", "text/plain"));

        await using var content = await _fixture.Drive.DownloadAsync(Item("notes.txt", "p", "d1", "text/plain"), null, Token);

        using var reader = new StreamReader(content.Stream);
        Assert.Equal("notes.txt", content.FileName);
        Assert.Equal("hello", await reader.ReadToEndAsync(Token));
    }

    [Fact]
    public async Task Should_export_a_Google_document_to_the_format_asked_and_name_it_after_it()
    {
        _fixture.Handler.Map(HttpMethod.Get, Api + "files/doc-1/export?mimeType=application/pdf", new FakeHttpResponse(HttpStatusCode.OK, "%PDF", "application/pdf"));
        var document = Item("Plan.docx", "p", "doc-1", "application/vnd.google-apps.document") with { IsGoogleDocument = true };

        await using var content = await _fixture.Drive.DownloadAsync(document, "PDF", Token);

        Assert.Equal("Plan.pdf", content.FileName);
        Assert.Equal("application/pdf", content.MimeType);
    }

    [Fact]
    public async Task Should_refuse_an_export_format_the_kind_does_not_offer_or_one_on_a_stored_file()
    {
        var sheet = Item("Budget", "p", "s-1", "application/vnd.google-apps.spreadsheet") with { IsGoogleDocument = true };

        var wrong = await Assert.ThrowsAsync<DriveToolException>(() => _fixture.Drive.DownloadAsync(sheet, "docx", Token));
        var stored = await Assert.ThrowsAsync<DriveToolException>(() => _fixture.Drive.DownloadAsync(Item("a.txt", "p"), "pdf", Token));

        Assert.Contains("xlsx, pdf, ods, csv", wrong.Message, StringComparison.Ordinal);
        Assert.Equal(DriveErrorCode.InvalidRequest, stored.Code);
        Assert.Empty(_fixture.Handler.Requests);
    }

    [Fact]
    public async Task Should_upload_through_a_resumable_session_without_sending_the_token_to_it()
    {
        var session = new Uri("https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&upload_id=xyz");
        _fixture.Handler.Map(HttpMethod.Get, Api + "files?q=", FakeHttpResponse.Json("""{"files":[]}"""));
        _fixture.Handler.Map(HttpMethod.Post, "https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&fields=", new FakeHttpResponse(HttpStatusCode.OK, "", Location: session));
        _fixture.Handler.Map(HttpMethod.Put, session.AbsoluteUri, FakeHttpResponse.Json(File("new-id", "report.txt", "text/plain", parent: "folder-id")));
        await using var bytes = new MemoryStream(Encoding.UTF8.GetBytes("report body"));

        var item = await _fixture.Drive.UploadAsync("folder-id", "report.txt", new DriveUpload(bytes, bytes.Length, "text/plain"), Token);

        Assert.Equal("new-id", item.Id);
        var open = Assert.Single(_fixture.Handler.RequestsTo(HttpMethod.Post, "https://www.googleapis.com/upload/"));
        Assert.Equal(DriveFixture.Bearer, open.Authorization);
        Assert.Equal("""{"name":"report.txt","parents":["folder-id"]}""", open.Body);
        var put = Assert.Single(_fixture.Handler.RequestsTo(HttpMethod.Put, "https://www.googleapis.com/upload/"));
        Assert.Null(put.Authorization);
        Assert.Equal("report body", put.Body);
    }

    [Fact]
    public async Task Should_refuse_a_name_already_taken_in_the_folder_before_uploading()
    {
        _fixture.Handler.Map(HttpMethod.Get, Api + "files?q=", FakeHttpResponse.Json($$"""{"files":[{{File("old-id", "report.txt", "text/plain")}}]}"""));
        await using var bytes = new MemoryStream([1, 2, 3]);

        var error = await Assert.ThrowsAsync<DriveToolException>(() => _fixture.Drive.UploadAsync("folder-id", "report.txt", new DriveUpload(bytes, 3, "text/plain"), Token));

        Assert.Equal(DriveErrorCode.Conflict, error.Code);
        Assert.Contains("old-id", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_fixture.Handler.Requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Should_move_by_swapping_parents_and_rename_in_the_same_call()
    {
        _fixture.Handler.Map(HttpMethod.Get, Api + "files?q=", FakeHttpResponse.Json("""{"files":[]}"""));
        _fixture.Handler.Map(HttpMethod.Patch, Api + "files/d1?", FakeHttpResponse.Json(File("d1", "final.txt", "text/plain", parent: "dest")));

        var moved = await _fixture.Drive.MoveAsync(Item("draft.txt", "src", "d1"), "dest", "final.txt", Token);

        Assert.Equal("dest", moved.ParentId);
        var patch = Assert.Single(_fixture.Handler.RequestsTo(HttpMethod.Patch, Api));
        Assert.Contains("addParents=dest&removeParents=src", patch.DecodedUri, StringComparison.Ordinal);
        Assert.Equal("""{"name":"final.txt"}""", patch.Body);
    }

    [Fact]
    public async Task Should_let_an_item_keep_its_own_name_when_it_only_moves()
    {
        _fixture.Handler.Map(HttpMethod.Get, Api + "files?q=", FakeHttpResponse.Json($$"""{"files":[{{File("d1", "draft.txt", "text/plain", parent: "dest")}}]}"""));
        _fixture.Handler.Map(HttpMethod.Patch, Api + "files/d1?", FakeHttpResponse.Json(File("d1", "draft.txt", "text/plain", parent: "dest")));

        var moved = await _fixture.Drive.MoveAsync(Item("draft.txt", "src", "d1"), "dest", newName: null, Token);

        Assert.Equal("dest", moved.ParentId);
    }

    [Fact]
    public async Task Should_trash_by_a_flag_and_purge_by_a_delete()
    {
        _fixture.Handler.Map(HttpMethod.Patch, Api + "files/d1?", FakeHttpResponse.Json("""{"id":"d1"}"""));
        _fixture.Handler.Map(HttpMethod.Delete, Api + "files/d2", new FakeHttpResponse(HttpStatusCode.NoContent, ""));

        await _fixture.Drive.TrashAsync("d1", Token);
        await _fixture.Drive.DeletePermanentlyAsync("d2", Token);

        Assert.Equal("""{"trashed":true}""", Assert.Single(_fixture.Handler.RequestsTo(HttpMethod.Patch, Api)).Body);
        Assert.Single(_fixture.Handler.RequestsTo(HttpMethod.Delete, Api + "files/d2"));
        Assert.True(_fixture.Drive.Capabilities.HasFlag(DriveCapabilities.PermanentDelete));
    }

    [Fact]
    public async Task Should_share_with_each_recipient_without_notifying_by_default_or_by_an_anyone_link()
    {
        _fixture.Handler.Map(HttpMethod.Post, Api + "files/d1/permissions", FakeHttpResponse.Json("""{"id":"p"}"""));
        var item = Item("a.txt", "p", "d1") with { WebUrl = "https://drive.google.com/file/d/d1/view" };

        var invited = await _fixture.Drive.ShareAsync(item, new DriveShareRequest { Recipients = ["a@example.com", "b@example.com"], CanEdit = true }, Token);
        var link = await _fixture.Drive.ShareAsync(item, new DriveShareRequest { PublicLink = true }, Token);

        var requests = _fixture.Handler.RequestsTo(HttpMethod.Post, Api + "files/d1/permissions");
        Assert.Equal(3, requests.Count);
        Assert.Contains("sendNotificationEmail=false", requests[0].DecodedUri, StringComparison.Ordinal);
        Assert.Equal("""{"type":"user","role":"writer","emailAddress":"a@example.com"}""", requests[0].Body);
        Assert.Equal("""{"type":"anyone","role":"reader"}""", requests[2].Body);
        Assert.Equal(["a@example.com", "b@example.com"], invited.Recipients);
        Assert.Equal(item.WebUrl, link.Link);
    }

    [Fact]
    public async Task Should_retry_once_with_a_refreshed_token_after_a_401()
    {
        _fixture.Handler.Map(HttpMethod.Post, "https://oauth2.googleapis.com/token", FakeHttpResponse.Json("""{"access_token":"renewed","expires_in":3600}"""));
        _fixture.Handler.Enqueue(new FakeHttpResponse(HttpStatusCode.Unauthorized, """{"error":{"code":401,"message":"Invalid Credentials"}}"""));
        _fixture.Handler.Enqueue(FakeHttpResponse.Json(File("d1", "a.txt", "text/plain")));

        var item = await _fixture.Drive.GetItemAsync("d1", Token);

        Assert.Equal("d1", item.Id);
        var calls = _fixture.Handler.RequestsTo(HttpMethod.Get, Api);
        Assert.Equal([DriveFixture.Bearer, "Bearer renewed"], calls.Select(call => call.Authorization));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, """{"error":{"code":404,"message":"File not found: x.","errors":[{"reason":"notFound"}]}}""", DriveErrorCode.ItemNotFound)]
    [InlineData(HttpStatusCode.Forbidden, """{"error":{"code":403,"message":"Rate","errors":[{"reason":"userRateLimitExceeded"}]}}""", DriveErrorCode.ServerError)]
    [InlineData(HttpStatusCode.Forbidden, """{"error":{"code":403,"message":"No","errors":[{"reason":"insufficientFilePermissions"}]}}""", DriveErrorCode.AuthenticationFailed)]
    [InlineData(HttpStatusCode.BadRequest, """{"error":{"code":400,"message":"Invalid Value","errors":[{"reason":"invalid"}]}}""", DriveErrorCode.InvalidRequest)]
    internal async Task Should_turn_API_errors_into_actionable_codes(HttpStatusCode status, string body, DriveErrorCode code)
    {
        _fixture.Handler.Enqueue(new FakeHttpResponse(status, body));

        var error = await Assert.ThrowsAsync<DriveToolException>(() => _fixture.Drive.GetItemAsync("x", Token));

        Assert.Equal(code, error.Code);
        Assert.StartsWith("Google Drive", error.Message, StringComparison.Ordinal);
    }

    private void MapRoot()
    {
        _fixture.Handler.Map(HttpMethod.Get, Api + "files/root?", FakeHttpResponse.Json(File("my-drive", "My Drive", Folder)));
        _fixture.Handler.Map(HttpMethod.Get, Api + "files?q='my-drive' in parents and name = 'Orkeon'", FakeHttpResponse.Json($$"""{"files":[{{File("orkeon-id", "Orkeon", Folder, parent: "my-drive")}}]}"""));
        _fixture.Handler.Map(HttpMethod.Get, Api + "files?q='orkeon-id' in parents and name = 'agents'", FakeHttpResponse.Json($$"""{"files":[{{File("agents-id", "agents", Folder, parent: "orkeon-id")}}]}"""));
    }

    private static string File(string id, string name, string mimeType, string? parent = null, string? size = null)
    {
        var parents = parent is null ? string.Empty : $$""","parents":["{{parent}}"]""";
        var bytes = size is null ? string.Empty : $$""","size":"{{size}}" """.TrimEnd();
        return $$"""{"id":"{{id}}","name":"{{name}}","mimeType":"{{mimeType}}","modifiedTime":"2026-10-16T08:00:00.000Z"{{parents}}{{bytes}}}""";
    }

    private static DriveItem Item(string name, string? parentId, string id = "x-id", string mimeType = "text/plain") => new()
    {
        Id = id,
        Name = name,
        IsFolder = false,
        MimeType = mimeType,
        ParentId = parentId,
    };
}
