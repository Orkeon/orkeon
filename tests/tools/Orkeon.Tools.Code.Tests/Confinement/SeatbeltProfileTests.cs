using Orkeon.Tools.Code.Confinement;

namespace Orkeon.Tools.Code.Tests.Confinement;

/// <summary>
/// The Seatbelt profile built from the VFS: everything denied first, the system read-only, each mount
/// after its rights, parents before their sub paths (the last matching rule wins), paths escaped as
/// SBPL strings, the network off unless allowed. Pure: runs on every platform.
/// </summary>
public sealed class SeatbeltProfileTests
{
    private static string Build(
        IEnumerable<ConfinedPath>? paths = null,
        IEnumerable<string>? readOnlyPaths = null,
        bool allowNetwork = false)
        => SeatbeltProfile.Build(paths ?? [], readOnlyPaths ?? [], allowNetwork);

    private static int IndexOf(string profile, string text)
    {
        var index = profile.IndexOf(text, StringComparison.Ordinal);
        Assert.True(index >= 0, $"The profile does not hold '{text}':\n{profile}");
        return index;
    }

    /// <summary>The rule that starts with <paramref name="operation"/> and names <paramref name="filter"/>.</summary>
    private static int RuleIndex(string profile, string operation, string filter)
    {
        var start = 0;
        while (true)
        {
            var index = profile.IndexOf("(" + operation + "\n", start, StringComparison.Ordinal);
            Assert.True(index >= 0, $"No ({operation} ...) rule names {filter}:\n{profile}");
            var end = profile.IndexOf(")\n(", index, StringComparison.Ordinal);
            var rule = end < 0 ? profile[index..] : profile[index..end];
            if (rule.Contains(filter, StringComparison.Ordinal))
                return index;
            start = index + 1;
        }
    }

    [Fact]
    public void ShouldDenyEverythingFirst()
    {
        var profile = Build();

        Assert.StartsWith("(version 1)\n(deny default)\n", profile, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldLetTheShellRunProgramsAndSignalOnlyItsOwnSandbox()
    {
        var profile = Build();

        IndexOf(profile, "(allow process-fork)");
        IndexOf(profile, "(allow process-exec)");
        IndexOf(profile, "(allow signal (target same-sandbox))");
        IndexOf(profile, "(allow sysctl-read)");
        Assert.DoesNotContain("(allow signal)", profile, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldReadTheSystemDirectoriesOnly()
    {
        var profile = Build();

        foreach (var path in (string[])["/usr", "/bin", "/sbin", "/System", "/Library", "/private/etc", "/dev"])
            RuleIndex(profile, "allow file-read*", $"(subpath \"{path}\")");
        Assert.DoesNotContain("\"/private/tmp\"", profile, StringComparison.Ordinal);
        Assert.DoesNotContain("(subpath \"/private/var\")", profile, StringComparison.Ordinal);
        Assert.DoesNotContain("(subpath \"/Users", profile, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldLookUpOnlyTheMinimalMachServices_WhenTheNetworkIsOff()
    {
        var profile = Build();

        RuleIndex(profile, "allow mach-lookup", "(global-name \"com.apple.system.opendirectoryd.libinfo\")");
        Assert.DoesNotContain("com.apple.dnssd.service", profile, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldDenyTheNetwork_WhenItIsNotAllowed()
    {
        var profile = Build();

        Assert.EndsWith("(deny network*)\n", profile, StringComparison.Ordinal);
        Assert.DoesNotContain("(allow network", profile, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldAllowTheNetworkAndNameResolution_WhenAllowed()
    {
        var profile = Build(allowNetwork: true);

        Assert.EndsWith("(allow network*)\n", profile, StringComparison.Ordinal);
        Assert.DoesNotContain("(deny network", profile, StringComparison.Ordinal);
        RuleIndex(profile, "allow mach-lookup", "(global-name \"com.apple.dnssd.service\")");
        RuleIndex(profile, "allow file-read*", "(subpath \"/private/var/run/resolv.conf\")");
    }

    [Fact]
    public void ShouldReadAReadOnlyMountAndRefuseWritingIt()
    {
        var profile = Build([new ConfinedPath("/Users/me/docs", "/docs", ConfinedAccess.ReadOnly)]);

        var allow = RuleIndex(profile, "allow file-read*", "(subpath \"/Users/me/docs\")");
        var deny = RuleIndex(profile, "deny file-write*", "(subpath \"/Users/me/docs\")");
        Assert.True(deny > allow);
        Assert.DoesNotContain("(allow file-read* file-write*\n  (subpath \"/Users/me/docs\")", profile, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldReadAndWriteAReadWriteMount()
    {
        var profile = Build([new ConfinedPath("/Users/me/ws", "/workspace", ConfinedAccess.ReadWrite)]);

        RuleIndex(profile, "allow file-read* file-write*", "(subpath \"/Users/me/ws\")");
    }

    [Fact]
    public void ShouldDenyAHiddenSubPathAfterTheAllowOfItsMount()
    {
        var profile = Build(
        [
            // Given children first: the profile orders them parents first all the same.
            new ConfinedPath("/Users/me/ws/secrets", "/workspace/secrets", ConfinedAccess.HiddenDirectory),
            new ConfinedPath("/Users/me/ws/.env", "/workspace/.env", ConfinedAccess.HiddenFile),
            new ConfinedPath("/Users/me/ws", "/workspace", ConfinedAccess.ReadWrite),
        ]);

        var allow = RuleIndex(profile, "allow file-read* file-write*", "(subpath \"/Users/me/ws\")");
        var hiddenDirectory = RuleIndex(profile, "deny file-read* file-write*", "(subpath \"/Users/me/ws/secrets\")");
        var hiddenFile = RuleIndex(profile, "deny file-read* file-write*", "(literal \"/Users/me/ws/.env\")");
        Assert.True(hiddenDirectory > allow);
        Assert.True(hiddenFile > allow);
    }

    [Fact]
    public void ShouldKeepAReadOnlySubPathOfAReadWriteMountReadOnly()
    {
        var profile = Build(
        [
            new ConfinedPath("/Users/me/ws/vendor", "/workspace/vendor", ConfinedAccess.ReadOnly),
            new ConfinedPath("/Users/me/ws", "/workspace", ConfinedAccess.ReadWrite),
        ]);

        var allow = RuleIndex(profile, "allow file-read* file-write*", "(subpath \"/Users/me/ws\")");
        var deny = RuleIndex(profile, "deny file-write*", "(subpath \"/Users/me/ws/vendor\")");
        Assert.True(deny > allow);
    }

    [Fact]
    public void ShouldPlaceTheMountsAfterTheSystemAndTheNetworkLast()
    {
        var profile = Build([new ConfinedPath("/Users/me/ws", "/workspace", ConfinedAccess.ReadWrite)], ["/opt/homebrew"]);

        var system = RuleIndex(profile, "allow file-read*", "(subpath \"/usr\")");
        var extra = RuleIndex(profile, "allow file-read*", "(subpath \"/opt/homebrew\")");
        var mount = RuleIndex(profile, "allow file-read* file-write*", "(subpath \"/Users/me/ws\")");
        var network = IndexOf(profile, "(deny network*)");
        Assert.True(system < extra && extra < mount && mount < network);
    }

    [Fact]
    public void ShouldAllowTheMetadataOfEveryAncestorOfAMount_NotItsContent()
    {
        var profile = Build([new ConfinedPath("/Users/me/ws", "/workspace", ConfinedAccess.ReadWrite)]);

        foreach (var ancestor in (string[])["/", "/Users", "/Users/me"])
            RuleIndex(profile, "allow file-read-metadata", $"(literal \"{ancestor}\")");
        Assert.DoesNotContain("(subpath \"/Users/me\")", profile, StringComparison.Ordinal);
        Assert.DoesNotContain("(subpath \"/Users\")", profile, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldNotOpenTheAncestorsOfAHiddenPath()
    {
        var profile = Build([new ConfinedPath("/Users/me/secret/file", "/x/file", ConfinedAccess.HiddenFile)]);

        Assert.DoesNotContain("(literal \"/Users/me/secret\")", profile, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldEscapeBackslashesAndDoubleQuotesInPaths()
    {
        var profile = Build([new ConfinedPath("/Users/me/a \"b\" \\c", "/workspace", ConfinedAccess.ReadWrite)]);

        RuleIndex(profile, "allow file-read* file-write*", "(subpath \"/Users/me/a \\\"b\\\" \\\\c\")");
    }

    [Fact]
    public void ShouldDropATrailingSlash()
    {
        var profile = Build([new ConfinedPath("/Users/me/ws/", "/workspace", ConfinedAccess.ReadWrite)]);

        RuleIndex(profile, "allow file-read* file-write*", "(subpath \"/Users/me/ws\")");
        Assert.DoesNotContain("\"/Users/me/ws/\"", profile, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("relative/path")]
    [InlineData("/with\0nul")]
    public void ShouldRefuseAPathThatIsNotAbsoluteOrHoldsANul(string path)
    {
        Assert.Throws<ArgumentException>(() => Build([new ConfinedPath(path, "/workspace", ConfinedAccess.ReadOnly)]));
        Assert.Throws<ArgumentException>(() => Build(readOnlyPaths: [path]));
    }

    [Theory]
    [InlineData("plain", "\"plain\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    [InlineData("a\\b", "\"a\\\\b\"")]
    [InlineData("it's", "\"it's\"")]
    public void ShouldQuoteAnSbplString(string value, string expected)
        => Assert.Equal(expected, SeatbeltProfile.Quote(value));
}
