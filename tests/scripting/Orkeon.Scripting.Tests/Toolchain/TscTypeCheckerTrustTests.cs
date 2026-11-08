using Orkeon.Scripting.Configuration;
using Orkeon.Scripting.Toolchain;

namespace Orkeon.Scripting.Tests.Toolchain;

/// <summary>Serial: it changes the process working directory.</summary>
[CollectionDefinition("WorkingDirectory", DisableParallelization = true)]
public sealed class WorkingDirectoryCollection;

/// <summary>
/// <see cref="TscTypeChecker.ResolveBinary"/> never runs a compiler found from the working directory: an
/// untrusted checkout would have <c>orkeon run --validate</c> execute its own binary.
/// </summary>
[Collection("WorkingDirectory")]
public sealed class TscTypeCheckerTrustTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "orkeon-tsc-trust-" + Guid.NewGuid().ToString("N"));
    private readonly string _previous = Directory.GetCurrentDirectory();

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_previous);
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best-effort temp cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort temp cleanup */ }
    }

    [Fact]
    public void ShouldNotResolveATscPlantedUnderTheWorkingDirectory()
    {
        var bin = Path.Combine(_dir, "tools", "scripting-typecheck", "node_modules", ".bin");
        Directory.CreateDirectory(bin);
        var planted = Path.Combine(bin, OperatingSystem.IsWindows() ? "tsc.cmd" : "tsc");
        File.WriteAllText(planted, "#!/bin/sh\nexit 0\n");
        Directory.SetCurrentDirectory(_dir);

        string? resolved = null;
        try { resolved = new TscTypeChecker(new ScriptingToolchainOptions()).ResolveBinary(); }
        catch (TscNotFoundException) { /* no trusted compiler on this machine: nothing was run */ }

        Assert.NotEqual(planted, resolved);
    }

    // SEC-6: a relative TscPath would resolve against the working directory; it is refused.
    [Fact]
    public void ShouldRefuseARelativeTscPath_EvenWhenTheWorkingDirectoryHoldsIt()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "tsc"), "#!/bin/sh\nexit 0\n");
        Directory.SetCurrentDirectory(_dir);

        var error = Assert.Throws<TscNotFoundException>(() =>
            new TscTypeChecker(new ScriptingToolchainOptions { TscPath = "tsc" }).ResolveBinary());

        Assert.Contains("relative", error.Message, StringComparison.Ordinal);
    }
}
