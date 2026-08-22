namespace Orkeon.Host.Tests;

/// <summary>
/// GATE-02: the crew loader reads through the VFS, so every hosted crew's directory must be
/// mounted. The first real-composition runner test caught the gap: the startup probe passed
/// on the physical path and every message then failed with "file not found" on the virtual
/// one — the documented example configuration could not load its own crew.
/// </summary>
public sealed class HostCrewMountsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"orkeon-mounts-test-{Guid.NewGuid():N}");

    public HostCrewMountsTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "crews", "support"));
        File.WriteAllText(Path.Combine(_dir, "crews", "billing.yaml"), "name: billing");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void A_crew_file_mounts_its_directory_and_a_crew_directory_mounts_itself()
    {
        var mounts = HostCrewMounts.For(
        [
            new HostedCrewOptions { Name = "billing", Path = Path.Combine(_dir, "crews", "billing.yaml") },
            new HostedCrewOptions { Name = "support", Path = Path.Combine(_dir, "crews", "support") },
        ]);

        var crewsDir = Path.Combine(_dir, "crews");
        var supportDir = Path.Combine(_dir, "crews", "support");
        Assert.Contains($"{crewsDir}:{crewsDir}:ro", mounts);
        Assert.Contains($"{supportDir}:{supportDir}:ro", mounts);
    }

    [Fact]
    public void Two_crews_in_one_directory_produce_one_mount()
    {
        var mounts = HostCrewMounts.For(
        [
            new HostedCrewOptions { Name = "a", Path = Path.Combine(_dir, "crews", "a.yaml") },
            new HostedCrewOptions { Name = "b", Path = Path.Combine(_dir, "crews", "b.yaml") },
        ]);

        Assert.Single(mounts);
    }

    [Fact]
    public void Mounts_are_read_only()
    {
        var mounts = HostCrewMounts.For(
            [new HostedCrewOptions { Name = "a", Path = Path.Combine(_dir, "crews", "a.yaml") }]);

        Assert.All(mounts, mount => Assert.EndsWith(":ro", mount, StringComparison.Ordinal));
    }
}
