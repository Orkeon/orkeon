using Orkeon.Scripting.Toolchain;

namespace Orkeon.Scripting.Tests.Toolchain;

/// <summary>
/// Verifies the repo-local candidate path probe handles every npm layout esbuild produces.
/// Regression: a Windows host clones the repo and runs <c>npm install</c> under
/// <c>tools/scripting-esbuild/</c>; npm materialises <c>@esbuild/win32-x64/esbuild.exe</c>
/// (no <c>bin/</c> subdirectory, name carries <c>.exe</c>). The previous resolver only
/// looked for <c>esbuild/bin/esbuild.exe</c> and therefore missed the install entirely,
/// failing every <c>Cli_*</c> scripting test on Windows even though the binary was present.
/// </summary>
public class EsbuildRepoLocalCandidateTests
{
    private static readonly string[] KnownOperatingSystems = ["win32", "linux", "darwin"];
    private static readonly string[] KnownArchitectures = ["x64", "arm64", "ia32"];

    [Fact]
    public void Candidates_AlwaysIncludeLegacyEsbuildBinPath()
    {
        var candidates = EsbuildTranspiler.BuildRepoLocalCandidates(
            binaryName: "esbuild",
            platformRid: null,
            windowsPackageLayout: false);

        Assert.Contains(
            candidates,
            parts => parts.SequenceEqual(new[]
            {
                "tools", "scripting-esbuild", "node_modules", "esbuild", "bin", "esbuild",
            }));
    }

    [Fact]
    public void Candidates_OnWindows_IncludeRootedExeUnderAtEsbuildPackage()
    {
        var candidates = EsbuildTranspiler.BuildRepoLocalCandidates(
            binaryName: "esbuild.exe",
            platformRid: "win32-x64",
            windowsPackageLayout: true);

        // Modern Windows layout: node_modules/@esbuild/win32-x64/esbuild.exe (NO bin/).
        Assert.Contains(
            candidates,
            parts => parts.SequenceEqual(new[]
            {
                "tools", "scripting-esbuild", "node_modules", "@esbuild", "win32-x64", "esbuild.exe",
            }));
    }

    [Fact]
    public void Candidates_OnUnix_IncludeBinUnderAtEsbuildPackage()
    {
        var candidates = EsbuildTranspiler.BuildRepoLocalCandidates(
            binaryName: "esbuild",
            platformRid: "linux-x64",
            windowsPackageLayout: false);

        // Modern Unix layout: node_modules/@esbuild/linux-x64/bin/esbuild
        Assert.Contains(
            candidates,
            parts => parts.SequenceEqual(new[]
            {
                "tools", "scripting-esbuild", "node_modules", "@esbuild", "linux-x64", "bin", "esbuild",
            }));
    }

    [Fact]
    public void Candidates_WhenPlatformIsUnknown_OnlyEmitLegacyPath()
    {
        // The legacy path must still be probed so the resolver works on platforms we don't
        // map to an RID (it gives operators a graceful path forward via a hand-placed copy).
        var candidates = EsbuildTranspiler.BuildRepoLocalCandidates(
            binaryName: "esbuild",
            platformRid: null,
            windowsPackageLayout: false);

        Assert.Single(candidates);
    }

    [Theory]
    [InlineData("win32-arm64", true, "esbuild.exe")]
    [InlineData("darwin-x64", false, "esbuild")]
    [InlineData("darwin-arm64", false, "esbuild")]
    [InlineData("linux-arm64", false, "esbuild")]
    public void Candidates_HonorBinarySubdirConventionPerPlatform(
        string rid, bool windowsLayout, string binaryName)
    {
        var candidates = EsbuildTranspiler.BuildRepoLocalCandidates(binaryName, rid, windowsLayout);

        // The platform-namespaced probe should always be present in addition to the legacy one.
        Assert.Equal(2, candidates.Count);
        // Platform-namespaced path comes FIRST so the native binary is preferred over
        // the legacy JS shim (the latter has `#!/usr/bin/env node` and breaks without Node).
        var platformProbe = candidates[0];

        Assert.Equal("@esbuild", platformProbe[3]);
        Assert.Equal(rid, platformProbe[4]);

        if (windowsLayout)
        {
            // Binary sits directly under the package root, no `bin/`.
            Assert.Equal(binaryName, platformProbe[5]);
            Assert.Equal(6, platformProbe.Length);
        }
        else
        {
            Assert.Equal("bin", platformProbe[5]);
            Assert.Equal(binaryName, platformProbe[6]);
            Assert.Equal(7, platformProbe.Length);
        }
    }

    /// <summary>
    /// Regression: on a Linux/WSL host without Node, the legacy
    /// <c>esbuild/bin/esbuild</c> wrapper (a <c>#!/usr/bin/env node</c> script) was
    /// resolved first and failed with "/usr/bin/env: 'node': No such file or directory".
    /// The platform-namespaced native binary must take precedence so the toolchain is
    /// Node-independent.
    /// </summary>
    [Fact]
    public void Candidates_PlatformNamespaced_TakesPrecedenceOverLegacyShim()
    {
        var candidates = EsbuildTranspiler.BuildRepoLocalCandidates(
            binaryName: "esbuild",
            platformRid: "linux-x64",
            windowsPackageLayout: false);

        Assert.Equal(2, candidates.Count);
        Assert.Equal("@esbuild", candidates[0][3]);
        Assert.Equal("esbuild", candidates[1][3]); // legacy bucket
        Assert.Equal("bin", candidates[1][4]);
    }

    [Fact]
    public void GetEsbuildPlatformRid_ReturnsCurrentHostMapping()
    {
        var rid = EsbuildTranspiler.GetEsbuildPlatformRid();

        // We can't assert an exact value (depends on the test runner), but we can assert
        // it follows the <os>-<arch> shape esbuild publishes under @esbuild/*.
        Assert.NotNull(rid);
        Assert.Contains("-", rid);
        var parts = rid!.Split('-');
        Assert.Equal(2, parts.Length);
        Assert.Contains(parts[0], KnownOperatingSystems);
        Assert.Contains(parts[1], KnownArchitectures);
    }
}
