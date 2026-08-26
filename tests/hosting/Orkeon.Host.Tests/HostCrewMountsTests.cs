using Orkeon.Domain.FileSystem;

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

    /// <summary>
    /// Nothing rejects two crews sharing a Name, and <c>CrewHostRegistry.Find</c> answers with
    /// the FIRST match (case-insensitively). The plan has to agree, or a run started on the
    /// first declaration would load the second one's definition.
    /// </summary>
    [Fact]
    public void Two_crews_sharing_a_name_resolve_to_the_first_declaration()
    {
        var plan = HostCrewMounts.For(
        [
            new HostedCrewOptions { Name = "support", Path = Path.Combine(_dir, "a", "support.yaml") },
            new HostedCrewOptions { Name = "Support", Path = Path.Combine(_dir, "b", "support.yaml") },
        ]);

        var first = Path.GetFullPath(Path.Combine(_dir, "a"));
        var firstRoot = FileSystemMount.Parse(
            plan.Mounts.Single(m => FileSystemMount.TryGetBasePath(m) == first)).VirtualPath;

        Assert.Equal($"{firstRoot}/support.yaml", plan.VirtualPaths["support"]);
        Assert.Equal($"{firstRoot}/support.yaml", plan.VirtualPaths["Support"]);
    }

    /// <summary>
    /// The roots the daemon refuses an operator <c>--mount</c> on: one per mount, and exactly
    /// the virtual path each mount claims.
    /// </summary>
    [Fact]
    public void The_plan_names_the_roots_it_reserves()
    {
        var plan = HostCrewMounts.For(
        [
            new HostedCrewOptions { Name = "billing", Path = Path.Combine(_dir, "crews", "billing.yaml") },
            new HostedCrewOptions { Name = "support", Path = Path.Combine(_dir, "crews", "support") },
        ]);

        Assert.Equal(
            [.. plan.Mounts.Select(m => FileSystemMount.Parse(m).VirtualPath)],
            plan.Roots);
    }

    /// <summary>
    /// A crew directory holding a ';' or a ':' is legal on the operator's disk; the spec that
    /// mounts it has to survive its own parser, which is what <c>FileSystemMount.Quote</c> is
    /// for. Built bare, the ';' split the spec into an override clause and the daemon refused
    /// to start.
    /// </summary>
    [Fact]
    public void A_crew_directory_needing_quotes_still_produces_a_parsable_mount()
    {
        var odd = Path.Combine(_dir, "crews;2026");
        Directory.CreateDirectory(odd);

        var plan = HostCrewMounts.For(
            [new HostedCrewOptions { Name = "a", Path = Path.Combine(odd, "a.yaml") }]);

        var mount = FileSystemMount.Parse(Assert.Single(plan.Mounts));
        Assert.Equal(Path.GetFullPath(odd), mount.BasePath);
        Assert.Equal(HostCrewMounts.VirtualPathPrefix, mount.VirtualPath);
        Assert.Empty(mount.Overrides);
    }
}
