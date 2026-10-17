using Orkeon.Tools.Drive.Drives;
using Orkeon.Tools.Drive.Tests.Doubles;
using Orkeon.Tools.Drive.Tests.Fixtures;

namespace Orkeon.Tools.Drive.Tests.Tools;

/// <summary>The drive tools that change a drive, through the tool protocol.</summary>
public sealed class DriveChangingToolsTests
{
    [Fact]
    public async Task Should_upload_a_VFS_file_to_the_root_with_its_name_and_type()
    {
        using var fixture = new DriveToolFixture();
        fixture.Files.AddFile("/output/report.pdf", [1, 2, 3, 4]);

        var result = ToolResults.Success(await fixture.CallAsync("drive_upload", ("path", "/output/report.pdf")));

        Assert.Equal(false, result["replaced"]);
        Assert.Equal("report.pdf", ToolResults.Object(result, "item")["name"]);
        Assert.Equal([$"Upload {FakeDrive.RootId} report.pdf application/pdf 4"], fixture.Drive().Calls);
    }

    [Fact]
    public async Task Should_replace_the_content_of_an_existing_file_keeping_its_type()
    {
        using var fixture = new DriveToolFixture();
        fixture.Drive().AddFile("f1", "data.csv", "old", "text/csv");
        fixture.Files.AddFile("/output/new.txt", "a,b"u8.ToArray());

        var result = ToolResults.Success(await fixture.CallAsync("drive_upload", ("path", "/output/new.txt"), ("replace_id", "f1")));

        Assert.Equal(true, result["replaced"]);
        Assert.Equal(["Replace f1 text/csv"], fixture.Drive().Calls);
        Assert.Equal("a,b"u8.ToArray(), fixture.Drive().Contents["f1"]);
    }

    [Fact]
    public async Task Should_refuse_an_upload_above_the_account_ceiling_a_reader_and_a_missing_file()
    {
        using var fixture = new DriveToolFixture();
        fixture.Files.AddFile("/output/big.bin", new byte[(1024 * 1024) + 1]);

        Assert.Contains("at most 1 MB", ToolResults.Failure(await fixture.CallAsync("drive_upload", ("path", "/output/big.bin"))), StringComparison.Ordinal);
        Assert.Contains("does not grant the Write right", ToolResults.Failure(await fixture.CallAsync("drive_upload", ("account", "reader"), ("path", "/output/big.bin"))), StringComparison.Ordinal);
        Assert.Contains("is not a file", ToolResults.Failure(await fixture.CallAsync("drive_upload", ("path", "/output/nothing.txt"))), StringComparison.Ordinal);
        Assert.Empty(fixture.Drive().Calls);
    }

    [Fact]
    public async Task Should_create_a_folder_in_a_folder_and_refuse_a_path_as_a_name()
    {
        using var fixture = new DriveToolFixture();
        fixture.Drive().AddFolder("d1", "Plans");

        ToolResults.Success(await fixture.CallAsync("drive_create_folder", ("parent_id", "d1"), ("name", "2027")));
        var error = ToolResults.Failure(await fixture.CallAsync("drive_create_folder", ("name", "a/b")));

        Assert.Equal(["CreateFolder d1 2027"], fixture.Drive().Calls);
        Assert.Contains("single name", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_move_and_rename_but_never_out_of_the_root_into_itself_or_the_root_itself()
    {
        using var fixture = new DriveToolFixture();
        fixture.Drive().AddFolder("d1", "Plans");
        fixture.Drive().AddFolder("d2", "Sub", parentId: "d1");
        fixture.Drive().AddFile("f1", "draft.txt", "x");

        ToolResults.Success(await fixture.CallAsync("drive_move", ("id", "f1"), ("destination_id", "d1"), ("new_name", "final.txt")));
        var outside = ToolResults.Failure(await fixture.CallAsync("drive_move", ("id", "f1"), ("destination_id", FakeDrive.OutsideId)));
        var cycle = ToolResults.Failure(await fixture.CallAsync("drive_move", ("id", "d1"), ("destination_id", "d2")));
        var root = ToolResults.Failure(await fixture.CallAsync("drive_move", ("id", "root"), ("new_name", "x")));

        Assert.Equal(["Move f1 d1 final.txt"], fixture.Drive().Calls);
        Assert.Contains("No item", outside, StringComparison.Ordinal);
        Assert.Contains("into itself", cycle, StringComparison.Ordinal);
        Assert.Contains("root folder cannot be moved", root, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_trash_by_default_and_purge_only_with_the_Purge_right_and_capability()
    {
        using var fixture = new DriveToolFixture();
        fixture.Drive().AddFile("f1", "a.txt", "x");
        fixture.Drive().AddFile("f2", "b.txt", "x");
        fixture.Drive("personal").AddFile("p1", "c.txt", "x");
        fixture.Drive("personal").Capabilities = DriveCapabilities.None;

        var trashed = ToolResults.Success(await fixture.CallAsync("drive_delete", ("id", "f1")));
        ToolResults.Success(await fixture.CallAsync("drive_delete", ("id", "f2"), ("permanent", true)));
        var personal = ToolResults.Failure(await fixture.CallAsync("drive_delete", ("account", "personal"), ("id", "p1"), ("permanent", true)));
        var reader = ToolResults.Failure(await fixture.CallAsync("drive_delete", ("account", "reader"), ("id", "x")));

        Assert.Equal(false, trashed["permanent"]);
        Assert.Equal(["Trash f1", "Purge f2"], fixture.Drive().Calls);
        Assert.Contains("personal OneDrive", personal, StringComparison.Ordinal);
        Assert.Contains("Delete right", reader, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_share_with_allowed_recipients_only_and_without_notifying_by_default()
    {
        using var fixture = new DriveToolFixture();
        fixture.Drive().AddFile("f1", "a.txt", "x");

        var shared = ToolResults.Success(await fixture.CallAsync("drive_share", ("id", "f1"), ("recipients", new List<string> { "Ann@Example.com", "boss@corp.test" }), ("role", "writer")));
        var refused = ToolResults.Failure(await fixture.CallAsync("drive_share", ("id", "f1"), ("recipients", new List<string> { "ann@example.com", "spy@evil.test" })));
        var invalid = ToolResults.Failure(await fixture.CallAsync("drive_share", ("id", "f1"), ("recipients", new List<string> { "Ann <ann@example.com>" })));

        Assert.Equal("writer", shared["role"]);
        var (_, request) = Assert.Single(fixture.Drive().Shares);
        Assert.Equal((true, false), (request.CanEdit, request.Notify));
        Assert.Contains("may not share with spy@evil.test", refused, StringComparison.Ordinal);
        Assert.DoesNotContain("ann@example.com,", refused, StringComparison.Ordinal);
        Assert.Contains("Not an e-mail address", invalid, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_create_a_public_link_only_when_the_account_allows_it()
    {
        using var fixture = new DriveToolFixture();
        fixture.Drive().AddFile("f1", "a.txt", "x");

        var refused = ToolResults.Failure(await fixture.CallAsync("drive_share", ("id", "f1"), ("public_link", true)));
        fixture.Options.Accounts["open"] = OpenAccount();
        var link = ToolResults.Success(await fixture.CallAsync("drive_share", ("account", "open"), ("id", "f9"), ("public_link", true)));

        Assert.Contains("does not allow public links", refused, StringComparison.Ordinal);
        Assert.Equal("https://drive.example/public/f9", link["link"]);
        Assert.Empty((System.Collections.IEnumerable)link["recipients"]!);

        Orkeon.Tools.Drive.Configuration.DriveAccountOptions OpenAccount()
        {
            var open = TestDriveAccounts.Google(DriveToolFixture.AllRights);
            open.Share.AllowPublicLinks = true;
            fixture.Drives.DriveOf(TestDriveAccounts.Resolve("open", open)).AddFile("f9", "public.txt", "x");
            return open;
        }
    }

    [Fact]
    public async Task Should_refuse_a_share_without_recipients_or_with_both_forms()
    {
        using var fixture = new DriveToolFixture();

        Assert.Contains("`recipients` is required", ToolResults.Failure(await fixture.CallAsync("drive_share", ("id", "f1"))), StringComparison.Ordinal);
        Assert.Contains("not both", ToolResults.Failure(await fixture.CallAsync("drive_share", ("id", "f1"), ("public_link", true), ("recipients", new List<string> { "a@example.com" }))), StringComparison.Ordinal);
        Assert.Contains("reader or writer", ToolResults.Failure(await fixture.CallAsync("drive_share", ("id", "f1"), ("role", "owner"), ("recipients", new List<string> { "a@example.com" }))), StringComparison.Ordinal);
    }
}
