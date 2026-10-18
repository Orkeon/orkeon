using Orkeon.Tools.Code.Confinement;
using Orkeon.Tools.Code.Sessions;

namespace Orkeon.Tools.Code.Tests.Confinement;

/// <summary>
/// <see cref="SeatbeltProbe.Canonicalize(string, Func{string, string?})"/>: the path Seatbelt matches,
/// every link along it resolved; and the profile of a session built from canonical paths.
/// </summary>
public sealed class SeatbeltProbeTests
{
    private static string Canonicalize(string path, Dictionary<string, string> links)
        => SeatbeltProbe.Canonicalize(path, candidate => links.GetValueOrDefault(candidate));

    [Fact]
    public void ShouldResolveAnAbsoluteLinkAtTheRoot()
    {
        var links = new Dictionary<string, string> { ["/var"] = "/private/var" };

        Assert.Equal("/private/var/folders/x/T/ws", Canonicalize("/var/folders/x/T/ws", links));
    }

    [Fact]
    public void ShouldResolveARelativeLinkFromItsDirectory()
    {
        var links = new Dictionary<string, string> { ["/tmp"] = "private/tmp" };

        Assert.Equal("/private/tmp/a", Canonicalize("/tmp/a", links));
    }

    [Fact]
    public void ShouldResolveLinksOfLinksAndDotDot()
    {
        var links = new Dictionary<string, string>
        {
            ["/home"] = "/System/Volumes/Data/home",
            ["/System/Volumes/Data/home/me/ws"] = "../shared/ws",
        };

        Assert.Equal("/System/Volumes/Data/home/shared/ws/src", Canonicalize("/home/me/./ws/src/", links));
    }

    [Fact]
    public void ShouldKeepAPathWithoutLinksAsWritten()
    {
        Assert.Equal("/Users/me/ws", Canonicalize("/Users/me/ws", []));
        Assert.Equal("/", Canonicalize("/", []));
    }

    [Fact]
    public void ShouldRefuseALoopOfLinks()
    {
        var links = new Dictionary<string, string> { ["/a"] = "/b", ["/b"] = "/a" };

        Assert.Throws<ArgumentException>(() => Canonicalize("/a/x", links));
    }

    [Fact]
    public void ShouldRefuseARelativePath()
        => Assert.Throws<ArgumentException>(() => Canonicalize("ws", []));

    [Fact]
    public void ShouldResolveTheLinksOfTheHostDisk()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The test makes POSIX symbolic links.");
        var root = Directory.CreateTempSubdirectory("orkeon-seatbelt-canon-").FullName;
        try
        {
            var real = Directory.CreateDirectory(Path.Combine(root, "real")).FullName;
            var link = Path.Combine(root, "link");
            Directory.CreateSymbolicLink(link, "real");

            Assert.Equal(SeatbeltProbe.Canonicalize(real), SeatbeltProbe.Canonicalize(link));
            Assert.EndsWith("/real/missing", SeatbeltProbe.Canonicalize(Path.Combine(link, "missing")), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ShouldNameTheCanonicalMountsAndTheScratchDirectoryInTheProfile()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The test makes POSIX symbolic links.");
        var root = Directory.CreateTempSubdirectory("orkeon-seatbelt-profile-").FullName;
        try
        {
            var real = Directory.CreateDirectory(Path.Combine(root, "real")).FullName;
            var link = Path.Combine(root, "link");
            Directory.CreateSymbolicLink(link, "real");
            var view = new ShellSessionView([new ConfinedPath(link, "/workspace", ConfinedAccess.ReadOnly)], [], false);

            var profile = SeatbeltShellSandbox.BuildProfile(view, "/scratch/session");

            Assert.Contains($"(subpath {SeatbeltProfile.Quote(SeatbeltProbe.Canonicalize(real))})", profile, StringComparison.Ordinal);
            Assert.DoesNotContain($"(subpath {SeatbeltProfile.Quote(link)})", profile, StringComparison.Ordinal);
            Assert.Contains("(allow file-read* file-write*\n  (subpath \"/scratch/session\")", profile, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ShouldReportSeatbeltUnavailable_OffMacOS()
    {
        Assert.SkipWhen(OperatingSystem.IsMacOS(), "On macOS the probe answers for the host.");

        var availability = SeatbeltProbe.Current();

        Assert.False(availability.IsAvailable);
        Assert.Contains("macOS only", availability.Reason!, StringComparison.Ordinal);
    }
}
