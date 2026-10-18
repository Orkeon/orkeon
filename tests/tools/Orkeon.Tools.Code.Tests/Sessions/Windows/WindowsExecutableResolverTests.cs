using Orkeon.Tools.Code.Sessions.Windows;

namespace Orkeon.Tools.Code.Tests.Sessions.Windows;

/// <summary>
/// <see cref="WindowsExecutableResolver"/>: PATH only and in order, <c>.exe</c> then <c>.com</c>, a batch
/// file only when the caller allows it, never a relative PATH entry.
/// </summary>
public sealed class WindowsExecutableResolverTests
{
    private const string Path = @"C:\Windows\System32;C:\Program Files\Git\cmd;C:\tools\";

    private static Func<string, bool> Existing(params string[] files)
        => file => files.Contains(file, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void ShouldFindTheExeOfABareName_InTheFirstPathDirectoryThatHasIt()
    {
        var found = WindowsExecutableResolver.Resolve("git", Path, Existing(@"C:\Program Files\Git\cmd\git.exe", @"C:\tools\git.exe"), out var error);

        Assert.Equal(@"C:\Program Files\Git\cmd\git.exe", found);
        Assert.Null(error);
    }

    [Fact]
    public void ShouldTryTheComAfterTheExe()
        => Assert.Equal(@"C:\Windows\System32\tree.com", WindowsExecutableResolver.Resolve("tree", Path, Existing(@"C:\Windows\System32\tree.com"), out _));

    [Fact]
    public void ShouldKeepAnExplicitExtension()
        => Assert.Equal(@"C:\tools\node.exe", WindowsExecutableResolver.Resolve("node.exe", Path, Existing(@"C:\tools\node.exe"), out _));

    [Theory]
    [InlineData("npm.cmd")]
    [InlineData("build.bat")]
    public void ShouldRefuseABatchFile_UnlessAllowed(string name)
    {
        var found = WindowsExecutableResolver.Resolve(name, Path, _ => true, out var error);

        Assert.Null(found);
        Assert.Contains("batch file", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldNotFindABatchFile_ForABareName()
        => Assert.Null(WindowsExecutableResolver.Resolve("npm", Path, Existing(@"C:\tools\npm.cmd"), out _));

    [Theory]
    [InlineData("npm.cmd", @"C:\tools\npm.cmd")]
    [InlineData("build.bat", @"C:\tools\build.bat")]
    public void ShouldKeepAnExplicitBatchFile_WhenAllowed(string name, string file)
        => Assert.Equal(file, WindowsExecutableResolver.Resolve(name, Path, Existing(file), out _, allowBatchFiles: true));

    [Fact]
    public void ShouldFindTheCmdOfABareName_WhenBatchFilesAreAllowed()
        => Assert.Equal(@"C:\tools\npm.cmd", WindowsExecutableResolver.Resolve("npm", Path, Existing(@"C:\tools\npm.cmd"), out _, allowBatchFiles: true));

    [Fact]
    public void ShouldPreferTheExeToABatchFile_InTheSameDirectory()
        => Assert.Equal(@"C:\tools\node.exe", WindowsExecutableResolver.Resolve("node", Path, Existing(@"C:\tools\node.cmd", @"C:\tools\node.exe"), out _, allowBatchFiles: true));

    [Theory]
    [InlineData(@"C:\tools\npm.cmd", true)]
    [InlineData("build.BAT", true)]
    [InlineData("git.exe", false)]
    [InlineData("cmd", false)]
    public void ShouldTellABatchFile(string path, bool expected)
        => Assert.Equal(expected, WindowsExecutableResolver.IsBatchFile(path));

    [Fact]
    public void ShouldSkipARelativePathEntry_SoTheWorkingDirectoryNeverProvidesTheProgram()
    {
        var found = WindowsExecutableResolver.Resolve("git", @".;bin;C:\tools", Existing(@".\git.exe", @"bin\git.exe", @"C:\tools\git.exe"), out _);

        Assert.Equal(@"C:\tools\git.exe", found);
    }

    [Fact]
    public void ShouldSayWhatWasNotFound()
    {
        var found = WindowsExecutableResolver.Resolve("cargo", Path, _ => false, out var error);

        Assert.Null(found);
        Assert.Equal("'cargo' was not found on PATH", error);
    }

    [Fact]
    public void ShouldAcceptAQuotedAndAUncPathEntry()
    {
        Assert.Equal(@"C:\My Tools\jq.exe", WindowsExecutableResolver.Resolve("jq", "\"C:\\My Tools\"", Existing(@"C:\My Tools\jq.exe"), out _));
        Assert.Equal(@"\\server\bin\jq.exe", WindowsExecutableResolver.Resolve("jq", @"\\server\bin", Existing(@"\\server\bin\jq.exe"), out _));
    }
}
