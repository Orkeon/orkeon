namespace Orkeon.Host.Tests;

/// <summary>
/// GATE-02: the crew loader reads through the VFS, so every hosted crew's directory must be
/// mounted. The first real-composition runner test caught the gap: the startup probe passed
/// on the physical path and every message then failed with "file not found" on the virtual
/// one — the documented example configuration could not load its own crew.
/// <para>
/// Since ADR-008 the mount is also under a <b>name</b>: a hosted agent is never handed the
/// operator's disk layout, so the plan carries the virtual spelling of each crew alongside
/// the mounts that make it resolvable.
/// </para>
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
        var plan = HostCrewMounts.For(
        [
            new HostedCrewOptions { Name = "billing", Path = Path.Combine(_dir, "crews", "billing.yaml") },
            new HostedCrewOptions { Name = "support", Path = Path.Combine(_dir, "crews", "support") },
        ]);

        var crewsDir = Path.Combine(_dir, "crews");
        var supportDir = Path.Combine(_dir, "crews", "support");
        Assert.Contains(plan.Mounts, m => m.StartsWith($"{crewsDir}:", StringComparison.Ordinal));
        Assert.Contains(plan.Mounts, m => m.StartsWith($"{supportDir}:", StringComparison.Ordinal));

        // A crew file is addressed inside its directory's mount; a crew directory is the mount.
        Assert.Equal($"{HostCrewMounts.VirtualPathPrefix}/billing.yaml", plan.VirtualPaths["billing"]);
        Assert.Equal($"{HostCrewMounts.VirtualPathPrefix}-1", plan.VirtualPaths["support"]);
    }

    [Fact]
    public void Two_crews_in_one_directory_produce_one_mount()
    {
        var plan = HostCrewMounts.For(
        [
            new HostedCrewOptions { Name = "a", Path = Path.Combine(_dir, "crews", "a.yaml") },
            new HostedCrewOptions { Name = "b", Path = Path.Combine(_dir, "crews", "b.yaml") },
        ]);

        Assert.Single(plan.Mounts);
        Assert.Equal($"{HostCrewMounts.VirtualPathPrefix}/a.yaml", plan.VirtualPaths["a"]);
        Assert.Equal($"{HostCrewMounts.VirtualPathPrefix}/b.yaml", plan.VirtualPaths["b"]);
    }

    [Fact]
    public void Mounts_are_read_only()
    {
        var plan = HostCrewMounts.For(
            [new HostedCrewOptions { Name = "a", Path = Path.Combine(_dir, "crews", "a.yaml") }]);

        Assert.All(plan.Mounts, mount => Assert.EndsWith(":ro", mount, StringComparison.Ordinal));
    }

    /// <summary>
    /// The regression this whole change exists for: an operator's disk path must never be
    /// usable as a virtual path, so no mount may be identity-mapped and every crew must be
    /// addressed by a name.
    /// </summary>
    [Fact]
    public void No_mount_is_identity_mapped_and_every_crew_is_addressed_by_name()
    {
        var plan = HostCrewMounts.For(
        [
            new HostedCrewOptions { Name = "billing", Path = Path.Combine(_dir, "crews", "billing.yaml") },
            new HostedCrewOptions { Name = "support", Path = Path.Combine(_dir, "crews", "support") },
        ]);

        Assert.All(plan.Mounts, mount =>
        {
            var parsed = Orkeon.Domain.FileSystem.FileSystemMount.Parse(mount);
            Assert.StartsWith(HostCrewMounts.VirtualPathPrefix, parsed.VirtualPath, StringComparison.Ordinal);
            Assert.NotEqual(parsed.BasePath, parsed.VirtualPath);
        });

        Assert.All(plan.VirtualPaths.Values, virtualPath =>
        {
            Assert.StartsWith("/", virtualPath, StringComparison.Ordinal);
            Assert.DoesNotContain(_dir, virtualPath, StringComparison.Ordinal);
        });
    }
}
