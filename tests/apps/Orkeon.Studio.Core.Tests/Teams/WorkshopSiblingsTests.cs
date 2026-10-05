using Orkeon.Studio.Core.Teams;

namespace Orkeon.Studio.Core.Tests.Teams;

/// <summary>
/// In a workshop folder (STUDIO-64), what goes with a team is indexed by its folder's name:
/// Rename moves the trees that exist with it, Delete moves the whole of them under
/// <c>archive/&lt;slug&gt;/</c>, Duplicate copies <c>settings/&lt;slug&gt;</c> and nothing
/// else. A plain catalogue — no <c>settings/</c> and <c>workbooks/</c> beside the teams root —
/// is not a workshop, and nothing of this touches it.
/// </summary>
public sealed class WorkshopSiblingsTests : IDisposable
{
    private readonly string _workshop = Path.Combine(Path.GetTempPath(), $"orkeon-siblings-{Guid.NewGuid():N}");

    private string TeamsRoot => Path.Combine(_workshop, "teams");

    public void Dispose()
    {
        if (!Directory.Exists(_workshop))
            return;

        // A folder a test made read-only is given back before it goes.
        if (!OperatingSystem.IsWindows())
        {
            foreach (var directory in Directory.EnumerateDirectories(_workshop, "*", SearchOption.AllDirectories))
                File.SetUnixFileMode(directory, OwnerAll);
        }
        Directory.Delete(_workshop, recursive: true);
    }

    private const UnixFileMode OwnerAll = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    /// <summary>Takes the write right off a folder, so that nothing can be made or moved in it — Unix only.</summary>
    private static void MakeReadOnly(string directory)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
    }

    private string At(params string[] relative) => Path.Combine([_workshop, .. relative]);

    private void Lay(params string[] relative)
    {
        foreach (var path in relative)
            Directory.CreateDirectory(At(path));
    }

    /// <summary>A whole workshop around the team « veille », one file in each of its trees.</summary>
    private void LayWorkshop()
    {
        Lay("teams", "workbooks", "tests", "settings", "mounts.test", "archive");
        Lay("teams/veille/crew", "workbooks/veille", "tests/veille", "settings/veille", "mounts.test/veille/input");
        File.WriteAllText(At("teams", "veille", "crew", "config.yaml"), "name: veille\n");
        File.WriteAllText(At("workbooks", "veille", "decisions.md"), "# Décisions\n");
        File.WriteAllText(At("tests", "veille", "evidence.md"), "# Preuves\n");
        File.WriteAllText(At("settings", "veille", "appsettings.json"), "{}\n");
        File.WriteAllText(At("mounts.test", "veille", "input", "a.txt"), "a\n");
    }

    [Fact]
    public void A_plain_catalogue_is_no_workshop_and_moves_nothing()
    {
        Lay("teams/veille", "settings/veille", "tests/veille");
        File.WriteAllText(At("settings", "veille", "appsettings.json"), "{}\n");

        var rename = WorkshopSiblings.FollowRename(TeamsRoot, "veille", "veille-2");
        var archive = WorkshopSiblings.Archive(TeamsRoot, "veille");
        var copy = WorkshopSiblings.CopySettings(TeamsRoot, "veille", "veille-copy");

        Assert.Empty(WorkshopSiblings.TakenTrees(TeamsRoot, "veille-2"));
        foreach (var result in new[] { rename, archive, copy })
        {
            Assert.True(result.Succeeded);
            Assert.Empty(result.Moved);
            Assert.Null(result.Destination);
        }

        Assert.True(Directory.Exists(At("teams", "veille")));
        Assert.True(Directory.Exists(At("settings", "veille")));
        Assert.True(Directory.Exists(At("tests", "veille")));
        Assert.False(Directory.Exists(At("settings", "veille-2")));
        Assert.False(Directory.Exists(At("settings", "veille-copy")));
        Assert.False(Directory.Exists(At("archive")));
    }

    [Fact]
    public void Rename_moves_the_trees_that_exist_and_refuses_a_taken_one()
    {
        Lay("teams", "workbooks", "tests", "settings", "mounts.test");
        Lay("teams/veille-2", "workbooks/veille", "settings/veille");
        File.WriteAllText(At("settings", "veille", "appsettings.json"), "{}\n");

        var moved = WorkshopSiblings.FollowRename(TeamsRoot, "veille", "veille-2");

        Assert.True(moved.Succeeded);
        Assert.Equal(["workbooks", "settings"], moved.Moved);
        Assert.True(File.Exists(At("settings", "veille-2", "appsettings.json")));
        Assert.True(Directory.Exists(At("workbooks", "veille-2")));
        Assert.False(Directory.Exists(At("workbooks", "veille")));
        Assert.False(Directory.Exists(At("settings", "veille")));
        Assert.False(Directory.Exists(At("tests", "veille-2")));
        Assert.False(Directory.Exists(At("mounts.test", "veille-2")));

        // The way back, with a settings folder already under the destination: refused, nothing moved.
        Lay("settings/veille");
        Assert.Equal(["settings"], WorkshopSiblings.TakenTrees(TeamsRoot, "veille"));
        var refused = WorkshopSiblings.FollowRename(TeamsRoot, "veille-2", "veille");

        Assert.False(refused.Succeeded);
        Assert.Equal(["settings"], refused.Taken);
        // Nothing moved: every tree of the team stayed, not only the one whose destination is taken.
        Assert.Equal(["workbooks", "settings"], refused.Kept);
        Assert.Empty(refused.Moved);
        Assert.True(Directory.Exists(At("workbooks", "veille-2")));
        Assert.False(Directory.Exists(At("workbooks", "veille")));
        Assert.True(File.Exists(At("settings", "veille-2", "appsettings.json")));
    }

    [Fact]
    public void A_failed_move_puts_the_others_back()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "A read-only folder refuses a move on Unix only.");
        LayWorkshop();
        // The last tree to move cannot: its mount set is read-only, so « veille-2 » cannot be made in it.
        MakeReadOnly(At("mounts.test"));

        var result = WorkshopSiblings.FollowRename(TeamsRoot, "veille", "veille-2");

        Assert.False(result.Succeeded);
        Assert.Empty(result.Moved);
        Assert.Empty(result.Taken);
        Assert.Equal(["workbooks", "tests", "settings", "mounts.test"], result.Kept);
        Assert.True(File.Exists(At("workbooks", "veille", "decisions.md")));
        Assert.True(File.Exists(At("tests", "veille", "evidence.md")));
        Assert.True(File.Exists(At("settings", "veille", "appsettings.json")));
        Assert.True(File.Exists(At("mounts.test", "veille", "input", "a.txt")));
        Assert.False(Directory.Exists(At("workbooks", "veille-2")));
        Assert.False(Directory.Exists(At("tests", "veille-2")));
        Assert.False(Directory.Exists(At("settings", "veille-2")));
    }

    [Fact]
    public void Archive_moves_the_five_trees_under_archive_slug_and_suffixes_a_taken_one()
    {
        LayWorkshop();

        var archived = WorkshopSiblings.Archive(TeamsRoot, "veille");

        Assert.True(archived.Succeeded);
        Assert.Equal(At("archive", "veille"), archived.Destination);
        Assert.Equal(["teams", "workbooks", "tests", "settings", "mounts.test"], archived.Moved);
        Assert.True(File.Exists(At("archive", "veille", "teams", "crew", "config.yaml")));
        Assert.True(File.Exists(At("archive", "veille", "workbooks", "decisions.md")));
        Assert.True(File.Exists(At("archive", "veille", "tests", "evidence.md")));
        Assert.True(File.Exists(At("archive", "veille", "settings", "appsettings.json")));
        Assert.True(File.Exists(At("archive", "veille", "mounts.test", "input", "a.txt")));
        Assert.False(Directory.Exists(At("teams", "veille")));
        Assert.False(Directory.Exists(At("workbooks", "veille")));
        Assert.False(Directory.Exists(At("tests", "veille")));
        Assert.False(Directory.Exists(At("settings", "veille")));
        Assert.False(Directory.Exists(At("mounts.test", "veille")));

        // A second « veille », archived while the first is still there: beside it, suffixed.
        Lay("teams/veille", "settings/veille");
        var again = WorkshopSiblings.Archive(TeamsRoot, "veille");

        Assert.True(again.Succeeded);
        Assert.Equal(At("archive", "veille-2"), again.Destination);
        Assert.Equal(["teams", "settings"], again.Moved);
        Assert.True(Directory.Exists(At("archive", "veille-2", "teams")));
        Assert.True(Directory.Exists(At("archive", "veille-2", "settings")));
        Assert.True(File.Exists(At("archive", "veille", "teams", "crew", "config.yaml")));
    }

    [Fact]
    public void Archive_puts_everything_back_when_a_tree_cannot_move()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "A read-only folder refuses a move on Unix only.");
        LayWorkshop();
        MakeReadOnly(At("mounts.test"));

        var result = WorkshopSiblings.Archive(TeamsRoot, "veille");

        Assert.False(result.Succeeded);
        Assert.Null(result.Destination);
        Assert.Empty(result.Moved);
        Assert.Equal(["teams", "workbooks", "tests", "settings", "mounts.test"], result.Kept);
        Assert.True(File.Exists(At("teams", "veille", "crew", "config.yaml")));
        Assert.True(File.Exists(At("settings", "veille", "appsettings.json")));
        Assert.False(Directory.Exists(At("archive", "veille")));
    }

    [Fact]
    public void Duplicate_copies_the_settings_alone()
    {
        LayWorkshop();

        var copied = WorkshopSiblings.CopySettings(TeamsRoot, "veille", "veille-copy");

        Assert.True(copied.Succeeded);
        Assert.Equal(["settings"], copied.Moved);
        Assert.Equal(At("settings", "veille-copy"), copied.Destination);
        Assert.True(File.Exists(At("settings", "veille-copy", "appsettings.json")));
        Assert.True(File.Exists(At("settings", "veille", "appsettings.json")));
        Assert.False(Directory.Exists(At("workbooks", "veille-copy")));
        Assert.False(Directory.Exists(At("tests", "veille-copy")));
        Assert.False(Directory.Exists(At("mounts.test", "veille-copy")));

        // A settings folder already under the copy's name is the user's: left alone, said as taken.
        var taken = WorkshopSiblings.CopySettings(TeamsRoot, "veille", "veille-copy");
        Assert.False(taken.Succeeded);
        Assert.Equal(["settings"], taken.Taken);

        // A team without a settings folder gives its copy none either.
        Lay("teams/nue");
        var none = WorkshopSiblings.CopySettings(TeamsRoot, "nue", "nue-copy");
        Assert.True(none.Succeeded);
        Assert.Empty(none.Moved);
        Assert.False(Directory.Exists(At("settings", "nue-copy")));
    }
}
