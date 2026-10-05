using Orkeon.Studio.Core.Teams;

namespace Orkeon.Studio.Core.Tests.Teams;

/// <summary>
/// A workshop folder is recognised by its layout (STUDIO-61): <c>settings/</c> and
/// <c>workbooks/</c> beside the teams root. Its trees for a team are the ones that exist, and
/// a mount set is <c>mounts.</c> plus a lowercase name — nothing else.
/// </summary>
public sealed class WorkshopLayoutTests : IDisposable
{
    private readonly string _workshop = Path.Combine(Path.GetTempPath(), $"orkeon-workshop-{Guid.NewGuid():N}");

    private string TeamsRoot => Path.Combine(_workshop, "teams");

    public void Dispose()
    {
        if (Directory.Exists(_workshop))
            Directory.Delete(_workshop, recursive: true);
    }

    private void Lay(params string[] relative)
    {
        foreach (var path in relative)
            Directory.CreateDirectory(Path.Combine(_workshop, path));
    }

    [Fact]
    public void A_root_whose_parent_holds_settings_and_workbooks_is_a_workshop()
    {
        Lay("teams", "settings", "workbooks");

        Assert.True(WorkshopLayout.IsWorkshop(TeamsRoot));
        Assert.True(WorkshopLayout.IsWorkshop(TeamsRoot + Path.DirectorySeparatorChar));
        Assert.Equal(_workshop, WorkshopLayout.RootOf(TeamsRoot));
    }

    [Fact]
    public void One_of_the_two_markers_alone_is_not_a_workshop()
    {
        Lay("teams", "settings");
        Assert.False(WorkshopLayout.IsWorkshop(TeamsRoot));

        Directory.Delete(Path.Combine(_workshop, "settings"));
        Lay("workbooks");
        Assert.False(WorkshopLayout.IsWorkshop(TeamsRoot));
    }

    [Fact]
    public void A_plain_catalog_is_no_workshop_and_a_missing_root_is_none_either()
    {
        Lay("teams", "teams/veille");
        Assert.False(WorkshopLayout.IsWorkshop(TeamsRoot));

        Assert.False(WorkshopLayout.IsWorkshop(Path.Combine(_workshop, "absent", "teams")));
    }

    [Fact]
    public void Trees_of_a_team_are_the_ones_that_exist_and_a_mount_set_is_spelled_mounts_dot_lowercase()
    {
        Lay(
            "teams/veille", "workbooks/veille", "settings/veille",
            "mounts.test/veille", "mounts.Test/veille", "mountsx/veille", "mounts.prod-2/veille", "mounts.prod-2/autre",
            "tests/autre");

        var trees = WorkshopLayout.TreesOf(TeamsRoot, "veille");

        Assert.Equal(
            [
                new WorkshopTree("teams", Path.Combine(_workshop, "teams", "veille")),
                new WorkshopTree("workbooks", Path.Combine(_workshop, "workbooks", "veille")),
                new WorkshopTree("settings", Path.Combine(_workshop, "settings", "veille")),
                new WorkshopTree("mounts.prod-2", Path.Combine(_workshop, "mounts.prod-2", "veille")),
                new WorkshopTree("mounts.test", Path.Combine(_workshop, "mounts.test", "veille")),
            ],
            trees);
    }

    [Fact]
    public void A_team_with_no_tree_lists_none_and_a_missing_workshop_lists_none()
    {
        Lay("teams", "settings", "workbooks");

        Assert.Empty(WorkshopLayout.TreesOf(TeamsRoot, "veille"));
        Assert.Empty(WorkshopLayout.TreesOf(Path.Combine(_workshop, "absent", "teams"), "veille"));
    }

    [Fact]
    public void The_settings_file_of_a_team_is_named_even_when_absent()
    {
        Lay("teams");

        var file = WorkshopLayout.SettingsFileOf(TeamsRoot, "veille");

        Assert.Equal(Path.Combine(_workshop, "settings", "veille", "appsettings.json"), file);
        Assert.False(File.Exists(file));
    }
}
