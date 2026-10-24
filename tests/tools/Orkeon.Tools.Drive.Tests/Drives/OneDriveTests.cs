using System.Net;
using System.Text;
using Orkeon.Tools.Drive.Configuration;
using Orkeon.Tools.Drive.Drives;
using Orkeon.Tools.Drive.Drives.Graph;
using Orkeon.Tools.Drive.Tests.Fixtures;

namespace Orkeon.Tools.Drive.Tests.Drives;

/// <summary>The OneDrive engine against a scripted Microsoft Graph.</summary>
public sealed class OneDriveTests : IDisposable
{
    private const string Graph = "https://graph.microsoft.com/v1.0/me/drive/";

    private readonly DriveFixture _fixture = new(TestDriveAccounts.Resolve("pro", TestDriveAccounts.OneDrive()));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Should_address_the_root_folder_by_its_path()
    {
        MapRoot();

        var root = await _fixture.Drive.GetRootAsync(Token);

        Assert.Equal("agents-id", root.Id);
        Assert.Equal("/Orkeon", root.ParentPath);
        Assert.Equal(DriveFixture.Bearer, Assert.Single(_fixture.Handler.Requests).Authorization);
    }

    [Fact]
    public async Task Should_report_a_missing_root_as_a_configuration_problem()
    {
        _fixture.Handler.Map(HttpMethod.Get, Graph + "root:/Orkeon/agents", new FakeHttpResponse(HttpStatusCode.NotFound, """{"error":{"code":"itemNotFound","message":"The resource could not be found."}}"""));

        var error = await Assert.ThrowsAsync<DriveToolException>(() => _fixture.Drive.GetRootAsync(Token));

        Assert.Equal(DriveErrorCode.InvalidConfiguration, error.Code);
        Assert.Contains("/Orkeon/agents", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/Orkeon/agents", true)]
    [InlineData("/orkeon/AGENTS/deep/er", true)]
    [InlineData("/Orkeon/agents-old", false)]
    [InlineData("/Orkeon", false)]
    public async Task Should_tell_an_item_under_the_root_by_its_parent_path(string parentPath, bool within)
    {
        MapRoot();
        var root = await _fixture.Drive.GetRootAsync(Token);

        Assert.Equal(within, await _fixture.Drive.IsWithinAsync(new DriveItem { Id = "i", Name = "n", IsFolder = false, ParentPath = parentPath }, root, Token));
    }

    [Fact]
    public async Task Should_climb_the_parent_ids_of_an_item_that_came_without_a_path()
    {
        MapRoot();
        _fixture.Handler.Map(HttpMethod.Get, Graph + "items/sub-id?$select=id,parentReference", FakeHttpResponse.Json("""{"id":"sub-id","parentReference":{"id":"agents-id"}}"""));
        var root = await _fixture.Drive.GetRootAsync(Token);

        Assert.True(await _fixture.Drive.IsWithinAsync(new DriveItem { Id = "i", Name = "n", IsFolder = false, ParentId = "sub-id" }, root, Token));
    }

    [Fact]
    public async Task Should_read_again_a_search_hit_whose_parent_reference_holds_the_drive_alone()
    {
        // DRIVE-07 O12.a: OneDrive personal hands search hits whose parentReference has no id nor path.
        MapRoot();
        _fixture.Handler.Map(HttpMethod.Get, Graph + "items/hit-id?$select=id,name", FakeHttpResponse.Json(FileJson("hit-id", "notes.txt", "/Orkeon/agents")));
        var root = await _fixture.Drive.GetRootAsync(Token);

        Assert.True(await _fixture.Drive.IsWithinAsync(new DriveItem { Id = "hit-id", Name = "notes.txt", IsFolder = false, DriveId = null }, root, Token));
    }

    [Fact]
    public async Task Should_compare_OneDrive_ids_without_regard_to_case()
    {
        MapRoot();
        var root = await _fixture.Drive.GetRootAsync(Token);

        Assert.True(await _fixture.Drive.IsWithinAsync(new DriveItem { Id = "i", Name = "n", IsFolder = false, ParentId = "AGENTS-ID" }, root, Token));
        Assert.Single(_fixture.Handler.Requests);
    }

    [Fact]
    public async Task Should_match_the_root_under_the_id_Graph_gives_back_for_a_parent_alias()
    {
        MapRoot();
        _fixture.Handler.Map(HttpMethod.Get, Graph + "items/alias-id?$select=id,parentReference", FakeHttpResponse.Json("""{"id":"agents-id","parentReference":{"id":"orkeon-id"}}"""));
        var root = await _fixture.Drive.GetRootAsync(Token);

        Assert.True(await _fixture.Drive.IsWithinAsync(new DriveItem { Id = "i", Name = "n", IsFolder = false, ParentId = "alias-id" }, root, Token));
    }

    [Fact]
    public async Task Should_keep_out_a_search_hit_whose_parents_climb_to_the_drive_root()
    {
        MapRoot();
        _fixture.Handler.Map(HttpMethod.Get, Graph + "items/elsewhere-id?$select=id,parentReference", FakeHttpResponse.Json("""{"id":"elsewhere-id","parentReference":{"id":"drive-root"}}"""));
        _fixture.Handler.Map(HttpMethod.Get, Graph + "items/drive-root?$select=id,parentReference", FakeHttpResponse.Json("""{"id":"drive-root"}"""));
        var root = await _fixture.Drive.GetRootAsync(Token);

        Assert.False(await _fixture.Drive.IsWithinAsync(new DriveItem { Id = "i", Name = "n", IsFolder = false, ParentId = "elsewhere-id", ParentPath = "/Elsewhere" }, root, Token));
    }

    [Fact]
    public void Should_normalize_Graph_paths_of_personal_and_business_drives()
    {
        Assert.Equal("/", OneDrive.NormalizePath("/drive/root:"));
        Assert.Equal("/My Files/a", OneDrive.NormalizePath("/drives/b!xyz/root:/My%20Files/a/"));
        Assert.Null(OneDrive.NormalizePath("/drive/items/abc"));
    }

    [Fact]
    public async Task Should_list_children_folders_first_and_resume_from_the_next_link_with_the_page_size_asked()
    {
        const string next = "https://graph.microsoft.com/v1.0/me/drive/items/f/children?$top=20&$skiptoken=abc";
        // The resumed page first: the handler answers from the first route whose prefix matches.
        _fixture.Handler.Map(HttpMethod.Get, Graph + "items/f/children?$top=7&$skiptoken=abc", FakeHttpResponse.Json("""{"value":[]}"""));
        _fixture.Handler.Map(HttpMethod.Get, Graph + "items/f/children?", FakeHttpResponse.Json(
            $$"""{"@odata.nextLink":"{{next}}","value":[{{FileJson("d1", "a.txt", "/Orkeon/agents")}},{{FolderJson("f2", "Zeta")}}]}"""));

        var first = await _fixture.Drive.ListAsync("f", 20, null, Token);
        var second = await _fixture.Drive.ListAsync("f", 7, first.Cursor, Token);

        Assert.Equal(["Zeta", "a.txt"], first.Items.Select(item => item.Name));
        Assert.Equal(next, first.Cursor);
        Assert.Empty(second.Items);
        Assert.Null(second.Cursor);
    }

    [Fact]
    public async Task Should_refuse_a_cursor_that_does_not_point_at_Graph()
    {
        var error = await Assert.ThrowsAsync<DriveToolException>(() => _fixture.Drive.ListAsync("f", 5, "https://evil.example.com/steal?$top=5", Token));

        Assert.Equal(DriveErrorCode.InvalidRequest, error.Code);
        Assert.Empty(_fixture.Handler.Requests);
    }

    [Fact]
    public async Task Should_search_words_from_the_root_and_filter_the_other_criteria_itself()
    {
        MapRoot();
        _fixture.Handler.Map(HttpMethod.Get, Graph + "items/agents-id/search(q='O''Brien')", FakeHttpResponse.Json(
            $$"""{"value":[{{FileJson("d1", "brief.pdf", "/Orkeon/agents", "application/pdf")}},{{FileJson("d2", "notes.txt", "/Orkeon/agents")}},{{FolderJson("f1", "pdfs")}}]}"""));

        var page = await _fixture.Drive.SearchAsync(new DriveSearchQuery { Text = "O'Brien", MimeType = "application/pdf", Folders = false }, 10, null, Token);

        Assert.Equal(["brief.pdf"], page.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task Should_refuse_a_raw_query_and_a_search_without_words()
    {
        var raw = await Assert.ThrowsAsync<DriveToolException>(() => _fixture.Drive.SearchAsync(new DriveSearchQuery { Text = "a", RawQuery = "x" }, 5, null, Token));
        var wordless = await Assert.ThrowsAsync<DriveToolException>(() => _fixture.Drive.SearchAsync(new DriveSearchQuery { MimeType = "image/" }, 5, null, Token));

        Assert.Equal(DriveErrorCode.Unsupported, raw.Code);
        Assert.Equal(DriveErrorCode.InvalidRequest, wordless.Code);
    }

    [Fact]
    public async Task Should_download_through_the_preauthenticated_address_without_the_token()
    {
        _fixture.Handler.Map(HttpMethod.Get, Graph + "items/d1?$select=id,@microsoft.graph.downloadUrl",
            FakeHttpResponse.Json("""{"id":"d1","@microsoft.graph.downloadUrl":"https://public.dm.files.1drv.com/y4m/abc"}"""));
        _fixture.Handler.Map(HttpMethod.Get, "https://public.dm.files.1drv.com/", new FakeHttpResponse(HttpStatusCode.OK, "bytes", "application/octet-stream"));

        await using var content = await _fixture.Drive.DownloadAsync(new DriveItem { Id = "d1", Name = "a.bin", IsFolder = false }, null, Token);

        Assert.Equal("a.bin", content.FileName);
        Assert.Null(Assert.Single(_fixture.Handler.RequestsTo(HttpMethod.Get, "https://public.dm.files.1drv.com/")).Authorization);
    }

    [Fact]
    public async Task Should_put_a_small_file_in_one_request_that_fails_on_a_taken_name()
    {
        _fixture.Handler.Map(HttpMethod.Put, Graph + "items/f:/report.txt:/content", FakeHttpResponse.Json(FileJson("new-id", "report.txt", "/Orkeon/agents")));
        await using var bytes = new MemoryStream(Encoding.UTF8.GetBytes("small"));

        var item = await _fixture.Drive.UploadAsync("f", "report.txt", new DriveUpload(bytes, bytes.Length, "text/plain"), Token);

        Assert.Equal("new-id", item.Id);
        var put = Assert.Single(_fixture.Handler.Requests);
        Assert.Contains("@microsoft.graph.conflictBehavior=fail", put.DecodedUri, StringComparison.Ordinal);
        Assert.Equal("small", put.Body);
        Assert.Equal(DriveFixture.Bearer, put.Authorization);
    }

    [Fact]
    public async Task Should_send_a_large_file_in_chunks_to_an_upload_session_without_the_token()
    {
        const string session = "https://api.onedrive.com/rup/abc";
        _fixture.Handler.Map(HttpMethod.Post, Graph + "items/f:/big.bin:/createUploadSession", FakeHttpResponse.Json($$"""{"uploadUrl":"{{session}}"}"""));
        _fixture.Handler.Enqueue(new FakeHttpResponse(HttpStatusCode.Accepted, """{"nextExpectedRanges":["10485760-"]}"""));
        _fixture.Handler.Enqueue(new FakeHttpResponse(HttpStatusCode.Created, FileJson("big-id", "big.bin", "/Orkeon/agents")));
        var payload = new byte[(10 * 1024 * 1024) + 1000];
        await using var bytes = new MemoryStream(payload);

        var item = await _fixture.Drive.UploadAsync("f", "big.bin", new DriveUpload(bytes, payload.Length, "application/octet-stream"), Token);

        Assert.Equal("big-id", item.Id);
        var chunks = _fixture.Handler.RequestsTo(HttpMethod.Put, session);
        Assert.Equal(["bytes 0-10485759/10486760", "bytes 10485760-10486759/10486760"], chunks.Select(chunk => chunk.ContentRange));
        Assert.All(chunks, chunk => Assert.Null(chunk.Authorization));
        Assert.Contains("\"fail\"", Assert.Single(_fixture.Handler.RequestsTo(HttpMethod.Post, Graph)).Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_move_and_rename_with_one_patch()
    {
        _fixture.Handler.Map(HttpMethod.Patch, Graph + "items/d1?", FakeHttpResponse.Json(FileJson("d1", "final.txt", "/Orkeon/agents/done")));

        await _fixture.Drive.MoveAsync(new DriveItem { Id = "d1", Name = "draft.txt", IsFolder = false }, "dest", "final.txt", Token);

        Assert.Equal("""{"@microsoft.graph.conflictBehavior":"fail","parentReference":{"id":"dest"},"name":"final.txt"}""", Assert.Single(_fixture.Handler.Requests).Body);
    }

    [Fact]
    public async Task Should_delete_to_the_recycle_bin_and_offer_no_purge_on_a_personal_account()
    {
        _fixture.Handler.Map(HttpMethod.Delete, Graph + "items/d1", new FakeHttpResponse(HttpStatusCode.NoContent, ""));

        await _fixture.Drive.TrashAsync("d1", Token);

        Assert.Single(_fixture.Handler.RequestsTo(HttpMethod.Delete, Graph + "items/d1"));
        Assert.False(_fixture.Drive.Capabilities.HasFlag(DriveCapabilities.PermanentDelete));
        using var business = new DriveFixture(TestDriveAccounts.Resolve("work", TestDriveAccounts.OneDrive(tenant: "organizations")));
        Assert.True(business.Drive.Capabilities.HasFlag(DriveCapabilities.PermanentDelete));
    }

    [Fact]
    public async Task Should_invite_with_sign_in_required_and_create_anonymous_links()
    {
        _fixture.Handler.Map(HttpMethod.Post, Graph + "items/d1/invite", FakeHttpResponse.Json("""{"value":[]}"""));
        _fixture.Handler.Map(HttpMethod.Post, Graph + "items/d1/createLink", FakeHttpResponse.Json("""{"link":{"webUrl":"https://1drv.ms/t/s!abc"}}"""));
        var item = new DriveItem { Id = "d1", Name = "a.txt", IsFolder = false, WebUrl = "https://onedrive.live.com/?id=d1" };

        await _fixture.Drive.ShareAsync(item, new DriveShareRequest { Recipients = ["a@example.com"] }, Token);
        var link = await _fixture.Drive.ShareAsync(item, new DriveShareRequest { PublicLink = true, CanEdit = true }, Token);

        Assert.Equal(
            """{"recipients":[{"email":"a@example.com"}],"roles":["read"],"requireSignIn":true,"sendInvitation":false}""",
            Assert.Single(_fixture.Handler.RequestsTo(HttpMethod.Post, Graph + "items/d1/invite")).Body);
        Assert.Equal("""{"type":"edit","scope":"anonymous"}""", Assert.Single(_fixture.Handler.RequestsTo(HttpMethod.Post, Graph + "items/d1/createLink")).Body);
        Assert.Equal("https://1drv.ms/t/s!abc", link.Link);
    }

    [Fact]
    public async Task Should_map_a_conflict_to_its_code()
    {
        _fixture.Handler.Enqueue(new FakeHttpResponse(HttpStatusCode.Conflict, """{"error":{"code":"nameAlreadyExists","message":"Name already exists"}}"""));

        var error = await Assert.ThrowsAsync<DriveToolException>(() => _fixture.Drive.CreateFolderAsync("f", "Reports", Token));

        Assert.Equal(DriveErrorCode.Conflict, error.Code);
        Assert.Contains("nameAlreadyExists", error.Message, StringComparison.Ordinal);
    }

    private void MapRoot() =>
        _fixture.Handler.Map(HttpMethod.Get, Graph + "root:/Orkeon/agents", FakeHttpResponse.Json(
            """{"id":"agents-id","name":"agents","folder":{"childCount":2},"parentReference":{"id":"orkeon-id","path":"/drive/root:/Orkeon"}}"""));

    private static string FileJson(string id, string name, string parentPath, string mimeType = "text/plain") =>
        $$$"""{"id":"{{{id}}}","name":"{{{name}}}","size":5,"file":{"mimeType":"{{{mimeType}}}"},"lastModifiedDateTime":"2026-10-16T08:00:00Z","parentReference":{"id":"p","path":"/drive/root:{{{parentPath}}}"}}""";

    private static string FolderJson(string id, string name) =>
        $$$"""{"id":"{{{id}}}","name":"{{{name}}}","folder":{"childCount":0},"parentReference":{"id":"p","path":"/drive/root:/Orkeon/agents"}}""";
}
