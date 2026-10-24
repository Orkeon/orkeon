using Orkeon.Tools.Code.Confinement;

namespace Orkeon.Tools.Code.Tests.Confinement;

/// <summary>
/// <see cref="BubblewrapProbe"/>: what a hidden path is, and an availability that is either a path or
/// a reason, never both. The probe itself runs <c>bwrap</c> once per process; its answer depends on
/// the host, so only its shape is held here.
/// </summary>
public sealed class BubblewrapProbeTests : IDisposable
{
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), $"orkeon-bwrap-probe-{Guid.NewGuid():N}");

    public BubblewrapProbeTests() => Directory.CreateDirectory(_scratch);

    public void Dispose()
    {
        if (Directory.Exists(_scratch))
            Directory.Delete(_scratch, recursive: true);
    }

    [Fact]
    public void ShouldHideADirectoryAsADirectory_WhenThePathIsOne()
    {
        Assert.Equal(ConfinedAccess.HiddenDirectory, BubblewrapProbe.HiddenKindOf(_scratch));
    }

    [Fact]
    public void ShouldHideAFileAsAFile_WhenThePathIsOne()
    {
        var file = Path.Combine(_scratch, "secret.txt");
        File.WriteAllText(file, "x");

        Assert.Equal(ConfinedAccess.HiddenFile, BubblewrapProbe.HiddenKindOf(file));
    }

    [Fact]
    public void ShouldHideNothing_WhenNothingIsThere()
    {
        Assert.Null(BubblewrapProbe.HiddenKindOf(Path.Combine(_scratch, "absent")));
    }

    [Fact]
    public void ShouldBeAPathOrAReason_WhenAvailabilityIsBuilt()
    {
        var available = BubblewrapAvailability.Available("/usr/bin/bwrap");
        var unavailable = BubblewrapAvailability.Unavailable("no user namespaces");

        Assert.True(available.IsAvailable);
        Assert.Null(available.Reason);
        Assert.False(unavailable.IsAvailable);
        Assert.Null(unavailable.ExecutablePath);
        Assert.Equal("no user namespaces", unavailable.Reason);
    }

    [Fact]
    public void ShouldAnswerOnceAndConsistently_WhenTheHostIsProbed()
    {
        var first = BubblewrapProbe.Current();

        Assert.Same(first, BubblewrapProbe.Current());
        Assert.True(first.IsAvailable ^ first.Reason is not null);
        if (first.IsAvailable)
            Assert.True(Path.IsPathRooted(first.ExecutablePath));
        else if (!OperatingSystem.IsLinux())
            Assert.Contains("Linux only", first.Reason, StringComparison.Ordinal);
    }
}
