using Orkeon.Domain.FileSystem;

namespace Orkeon.Domain.Tests.FileSystem;

/// <summary>
/// The one predicate every "is this path inside that directory?" question in the framework
/// goes through. Three copies of it existed, they disagreed, and each disagreement was a bug:
/// the PathValidator's copy was boundary-safe, the runners' <c>--allow-external-mounts</c>
/// guard was a bare <c>StartsWith</c>, and the two answered differently for exactly the paths
/// that matter — a sibling directory whose name extends the one being tested.
/// </summary>
public sealed class PhysicalPathContainmentTests
{
    private static string P(params string[] segments) =>
        Path.GetFullPath(Path.Combine([Path.GetTempPath(), .. segments]));

    [Fact]
    public void ADirectoryContainsItself()
    {
        Assert.True(PhysicalPathContainment.IsUnder(P("proj"), P("proj")));
    }

    [Fact]
    public void ATrailingSeparatorOnTheDirectoryChangesNothing()
    {
        Assert.True(PhysicalPathContainment.IsUnder(P("proj", "src"), P("proj") + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void ADescendantIsInside()
    {
        Assert.True(PhysicalPathContainment.IsUnder(P("proj", "src", "a.cs"), P("proj")));
    }

    /// <summary>
    /// The case the bare prefix compare got wrong, in the direction that hurts: the runner's
    /// guard read <c>~/proj-old/crew</c> as inside <c>~/proj</c>, never demanded
    /// <c>--allow-external-mounts</c>, never whitelisted the base path — and the
    /// boundary-safe validator then refused every file the crew touched, without ever naming
    /// the flag that would have fixed it.
    /// </summary>
    [Fact]
    public void ASiblingWhoseNameExtendsTheDirectoryIsOutside()
    {
        Assert.False(PhysicalPathContainment.IsUnder(P("proj-old", "crew"), P("proj")));
        Assert.False(PhysicalPathContainment.IsUnder(P("projx"), P("proj")));
    }

    [Fact]
    public void AnUnrelatedPathIsOutside()
    {
        Assert.False(PhysicalPathContainment.IsUnder(P("other", "a.cs"), P("proj")));
    }

    /// <summary>
    /// Windows paths are the same path in any casing; POSIX ones are not. A guard hardcoding
    /// <see cref="StringComparison.Ordinal"/> demanded an opt-in for <c>C:\Proj</c> against a
    /// working directory spelled <c>C:\proj</c>.
    /// </summary>
    [Fact]
    public void CaseFollowsThePlatform()
    {
        var mixedCase = PhysicalPathContainment.IsUnder(P("PROJ", "src"), P("proj"));

        Assert.Equal(OperatingSystem.IsWindows(), mixedCase);
        Assert.Equal(
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal,
            PhysicalPathContainment.Comparison);
    }
}
