using System.Net;
using Orkeon.Tools.Drive.Drives;
using Orkeon.Tools.Drive.Tests.Doubles;
using Orkeon.Tools.Drive.Tests.Fixtures;
using Orkeon.Tools.Drive.Tools;

namespace Orkeon.Tools.Drive.Tests.Tools;

/// <summary>
/// The defects the review of the drive family found, each held by a test: a forged cursor, an
/// item of another drive, an unreachable parent, a hostile download name, a partial share and a
/// large Google upload.
/// </summary>
public sealed class DriveReviewRegressionTests
{
    private const string GoogleApi = "https://www.googleapis.com/drive/v3/";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_cursor_that_lists_another_folder_shows_nothing_of_it()
    {
        using var fixture = new DriveToolFixture();
        fixture.Drive().AddFile("in", "mine.txt", "x");
        fixture.Drive().AddFile("secret", "salaries.csv", "a,b", parentId: FakeDrive.OutsideId);

        var result = ToolResults.Success(await fixture.CallAsync("drive_list", ("cursor", "folder:" + FakeDrive.OutsideId)));

        Assert.Empty(ToolResults.Objects(result, "items"));
    }

    [Fact]
    public async Task An_item_of_another_OneDrive_with_the_same_path_is_outside_the_root()
    {
        using var fixture = new DriveFixture(TestDriveAccounts.Resolve("pro", TestDriveAccounts.OneDrive()));
        fixture.Handler.Map(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me/drive/root:/Orkeon/agents", FakeHttpResponse.Json(
            """{"id":"agents-id","name":"agents","folder":{},"parentReference":{"id":"o","driveId":"mine","path":"/drive/root:/Orkeon"}}"""));
        var root = await fixture.Drive.GetRootAsync(Token);

        var foreign = new DriveItem { Id = "f", Name = "x", IsFolder = false, ParentPath = "/Orkeon/agents", DriveId = "theirs" };
        var own = foreign with { DriveId = "mine" };

        Assert.False(await fixture.Drive.IsWithinAsync(foreign, root, Token));
        Assert.True(await fixture.Drive.IsWithinAsync(own, root, Token));
    }

    [Fact]
    public async Task An_item_whose_parent_cannot_be_opened_is_outside_the_root_rather_than_an_error()
    {
        using var fixture = new DriveFixture(TestDriveAccounts.Resolve("perso", TestDriveAccounts.Google(root: "Orkeon")));
        fixture.Handler.Map(HttpMethod.Get, GoogleApi + "files/root?", FakeHttpResponse.Json(
            """{"id":"my-drive","name":"My Drive","mimeType":"application/vnd.google-apps.folder"}"""));
        fixture.Handler.Map(HttpMethod.Get, GoogleApi + "files?q=", FakeHttpResponse.Json(
            """{"files":[{"id":"orkeon-id","name":"Orkeon","mimeType":"application/vnd.google-apps.folder","parents":["my-drive"]}]}"""));
        fixture.Handler.Map(HttpMethod.Get, GoogleApi + "files/hidden-parent?fields=id,parents", new FakeHttpResponse(HttpStatusCode.NotFound,
            """{"error":{"code":404,"message":"File not found","errors":[{"reason":"notFound"}]}}"""));
        var root = await fixture.Drive.GetRootAsync(Token);

        var within = await fixture.Drive.IsWithinAsync(new DriveItem { Id = "x", Name = "x", IsFolder = false, ParentId = "hidden-parent" }, root, Token);

        Assert.False(within);
    }

    [Theory]
    [InlineData("invoice‮fdp.exe")]
    [InlineData("CON.txt")]
    public void A_hostile_download_name_is_neither_reversed_nor_a_device(string name)
    {
        var safe = FileNames.Sanitize(name);

        Assert.DoesNotContain('‮', safe);
        Assert.NotEqual("CON", Path.GetFileNameWithoutExtension(safe), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_share_that_fails_partway_names_who_already_has_access()
    {
        using var fixture = new DriveFixture(TestDriveAccounts.Resolve("perso", TestDriveAccounts.Google()));
        fixture.Handler.Enqueue(FakeHttpResponse.Json("""{"id":"p1"}"""));
        fixture.Handler.Enqueue(new FakeHttpResponse(HttpStatusCode.BadRequest, """{"error":{"code":400,"message":"Bad","errors":[{"reason":"invalid"}]}}"""));
        var item = new DriveItem { Id = "d1", Name = "a.txt", IsFolder = false };

        var error = await Assert.ThrowsAsync<DriveToolException>(() =>
            fixture.Drive.ShareAsync(item, new DriveShareRequest { Recipients = ["a@example.com", "b@example.com"] }, Token));

        Assert.Contains("Already shared with: a@example.com.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_large_Google_upload_goes_in_8_MiB_chunks_to_the_session()
    {
        using var fixture = new DriveFixture(TestDriveAccounts.Resolve("perso", TestDriveAccounts.Google()));
        var session = new Uri("https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&upload_id=big");
        fixture.Handler.Map(HttpMethod.Get, GoogleApi + "files?q=", FakeHttpResponse.Json("""{"files":[]}"""));
        fixture.Handler.Map(HttpMethod.Post, "https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&fields=", new FakeHttpResponse(HttpStatusCode.OK, "", Location: session));
        fixture.Handler.Enqueue(new FakeHttpResponse((HttpStatusCode)308, ""));
        fixture.Handler.Enqueue(FakeHttpResponse.Json("""{"id":"big-id","name":"big.bin","mimeType":"application/octet-stream"}"""));
        var payload = new byte[(8 * 1024 * 1024) + 10];
        await using var bytes = new MemoryStream(payload);

        var item = await fixture.Drive.UploadAsync("folder", "big.bin", new DriveUpload(bytes, payload.Length, "application/octet-stream"), Token);

        Assert.Equal("big-id", item.Id);
        var chunks = fixture.Handler.RequestsTo(HttpMethod.Put, "https://www.googleapis.com/upload/");
        Assert.Equal(["bytes 0-8388607/8388618", "bytes 8388608-8388617/8388618"], chunks.Select(chunk => chunk.ContentRange));
        Assert.All(chunks, chunk => Assert.Null(chunk.Authorization));
    }
}
