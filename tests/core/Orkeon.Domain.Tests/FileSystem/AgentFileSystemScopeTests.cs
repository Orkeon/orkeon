using Orkeon.Domain.Execution;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Domain.Tests.FileSystem;

/// <summary>#349: an agent's file-system scope selects and narrows the mounts, never widens them.</summary>
public sealed class AgentFileSystemScopeTests
{
    private static readonly IReadOnlyList<MountInfo> Mounts =
    [
        new("/workspace", FileAccessRights.ReadWrite, [new SubPathOverride("secrets", FileAccessRights.ReadOnly)]),
        new("/docs", FileAccessRights.ReadOnly, []),
        new("/output", FileAccessRights.ReadWrite, []),
    ];

    private static AgentFileSystemScope Resolve(params FileSystemScopeEntry[] entries)
        => AgentFileSystemScope.Resolve("writer", entries, Mounts);

    [Theory]
    [Trait("BR", "#349")]
    [InlineData("ro", FileAccessRights.ReadOnly)]
    [InlineData("RW", FileAccessRights.ReadWrite)]
    [InlineData("rwnd", FileAccessRights.ReadWriteNoDelete)]
    public void ShouldParseAccess_WhenValueIsKnown(string value, FileAccessRights expected)
        => Assert.Equal(expected, FileSystemScopeEntry.ParseAccess(value));

    [Fact]
    [Trait("BR", "#349")]
    public void ShouldRefuseAccess_WhenValueIsUnknown()
    {
        var error = Assert.Throws<FormatException>(() => FileSystemScopeEntry.ParseAccess("all"));

        Assert.Contains("ro, rw, rwnd", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "#349")]
    public void ShouldGrantMountRights_WhenEntryDeclaresNoAccess()
    {
        var scope = Resolve(new FileSystemScopeEntry("/workspace/project"));

        Assert.Equal(FileAccessRights.ReadWrite, scope.RightsAt("/workspace/project/src/a.cs"));
        Assert.Equal(FileAccessRights.None, scope.RightsAt("/workspace/other/a.cs"));
        Assert.Equal(FileAccessRights.None, scope.RightsAt("/output/a.md"));
    }

    [Fact]
    [Trait("BR", "#349")]
    public void ShouldNarrowRights_WhenEntryDeclaresAccess()
    {
        var scope = Resolve(new FileSystemScopeEntry("/workspace", FileAccessRights.ReadOnly), new FileSystemScopeEntry("/output"));

        Assert.True(scope.Allows("/workspace/a.cs", FileAccessRights.Read));
        Assert.False(scope.Allows("/workspace/a.cs", FileAccessRights.Write));
        Assert.True(scope.Allows("/output/report.md", FileAccessRights.Write));
    }

    [Fact]
    [Trait("BR", "#349")]
    public void ShouldTakeLongestEntry_WhenEntriesNest()
    {
        var scope = Resolve(new FileSystemScopeEntry("/workspace", FileAccessRights.ReadOnly), new FileSystemScopeEntry("/workspace/out"));

        Assert.Equal(FileAccessRights.ReadOnly, scope.RightsAt("/workspace/src/a.cs"));
        Assert.Equal(FileAccessRights.ReadWrite, scope.RightsAt("/workspace/out/a.md"));
    }

    [Fact]
    [Trait("BR", "#349")]
    public void ShouldRefuseEntry_WhenPathIsUnderNoMount()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Resolve(new FileSystemScopeEntry("/nowhere")));

        Assert.Contains("'writer'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'/nowhere'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "#349")]
    public void ShouldRefuseEntry_WhenAccessExceedsMount()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Resolve(new FileSystemScopeEntry("/docs", FileAccessRights.ReadWrite)));

        Assert.Contains("'/docs'", error.Message, StringComparison.Ordinal);
        Assert.Contains("ro", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "#349")]
    public void ShouldRefuseEntry_WhenAccessExceedsMountOverride()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Resolve(new FileSystemScopeEntry("/workspace/secrets", FileAccessRights.ReadWrite)));

        Assert.Contains("'/workspace/secrets'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("BR", "#349")]
    [InlineData("workspace")]
    [InlineData("/workspace/../etc")]
    public void ShouldRefuseEntry_WhenPathIsNotAbsoluteOrClimbs(string path)
        => Assert.Throws<InvalidOperationException>(() => Resolve(new FileSystemScopeEntry(path)));

    [Fact]
    [Trait("BR", "#349")]
    public void ShouldRefuseScope_WhenListIsEmpty()
        => Assert.Throws<InvalidOperationException>(() => Resolve());

    [Fact]
    [Trait("BR", "#349")]
    public void ShouldRefuseChildEntry_WhenOutsideParentScope()
    {
        var parent = Resolve(new FileSystemScopeEntry("/workspace/project"));

        var error = Assert.Throws<InvalidOperationException>(
            () => AgentFileSystemScope.Resolve("child", [new FileSystemScopeEntry("/output")], Mounts, parent));

        Assert.Contains("'child'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'writer'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "#349")]
    public void ShouldRefuseChildEntry_WhenAccessExceedsParentScope()
    {
        var parent = Resolve(new FileSystemScopeEntry("/workspace", FileAccessRights.ReadOnly));

        Assert.Throws<InvalidOperationException>(
            () => AgentFileSystemScope.Resolve("child", [new FileSystemScopeEntry("/workspace/a", FileAccessRights.ReadWrite)], Mounts, parent));
    }

    [Fact]
    [Trait("BR", "#349")]
    public void ShouldStayBoundedByParent_WhenChildNarrows()
    {
        var parent = Resolve(new FileSystemScopeEntry("/workspace", FileAccessRights.ReadOnly));

        var child = AgentFileSystemScope.Resolve("child", [new FileSystemScopeEntry("/workspace/src")], Mounts, parent);

        Assert.Equal(FileAccessRights.ReadOnly, child.RightsAt("/workspace/src/a.cs"));
        Assert.Equal(FileAccessRights.None, child.RightsAt("/workspace/tests/a.cs"));
    }

    [Fact]
    [Trait("BR", "#349")]
    public void ShouldNameAgentAndScope_WhenDescribingDenial()
    {
        var scope = Resolve(new FileSystemScopeEntry("/workspace/project"), new FileSystemScopeEntry("/output", FileAccessRights.ReadOnly));

        var denial = scope.DescribeDenial("/workspace/other/a.cs", FileAccessRights.Read);

        Assert.StartsWith("Access denied for '/workspace/other/a.cs'", denial, StringComparison.Ordinal);
        Assert.Contains("agent 'writer'", denial, StringComparison.Ordinal);
        Assert.Contains("Its scope: /output (ro), /workspace/project.", denial, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "#349")]
    public void ShouldListOneMountPerEntry_WhenNarrowingMounts()
    {
        var scope = Resolve(new FileSystemScopeEntry("/workspace"), new FileSystemScopeEntry("/output", FileAccessRights.ReadOnly));

        var narrowed = scope.Narrow(Mounts);

        Assert.Equal(["/output", "/workspace"], narrowed.Select(m => m.VirtualPath).Order(StringComparer.Ordinal));
        Assert.Equal(FileAccessRights.ReadOnly, narrowed.Single(m => m.VirtualPath == "/output").DefaultRights);
        var workspace = narrowed.Single(m => m.VirtualPath == "/workspace");
        var secrets = Assert.Single(workspace.Overrides);
        Assert.Equal("secrets", secrets.RelativePath.Trim('/'));
        Assert.Equal(FileAccessRights.ReadOnly, secrets.Rights);
    }

    [Fact]
    [Trait("BR", "#349")]
    public async System.Threading.Tasks.Task ShouldEnforceScope_OnlyDuringToolCallOfItsRun()
    {
        var scope = Resolve(new FileSystemScopeEntry("/workspace"));
        await using var run = AgentRunScope.Begin("id", "writer", null, scope);

        Assert.Null(AgentRunScope.EnforcedFileSystem);
        using (AgentRunScope.EnterToolCall())
        {
            Assert.Same(scope, AgentRunScope.EnforcedFileSystem);
            await using var nested = AgentRunScope.Begin("child", "child", null, null);
            Assert.Null(AgentRunScope.EnforcedFileSystem);
        }
        Assert.Null(AgentRunScope.EnforcedFileSystem);
    }

    [Fact]
    [Trait("BR", "#349")]
    public void ShouldReadBackslashAsTheHostDoes_WhenCheckedPathHoldsOne()
    {
        var scope = Resolve(new FileSystemScopeEntry("/output/notes"));

        // Off Windows the file system keeps a backslash in the name: '/output/notes\x.md' is the file
        // 'notes\x.md' at the root of /output, outside the scope. On Windows it is /output/notes/x.md.
        var expected = OperatingSystem.IsWindows() ? FileAccessRights.ReadWrite : FileAccessRights.None;
        Assert.Equal(expected, scope.RightsAt("/output/notes\\x.md"));
        Assert.Equal(FileAccessRights.ReadWrite, scope.RightsAt("/output/notes/x.md"));
    }

    [Theory]
    [Trait("BR", "#349")]
    [InlineData("/output/REPORT/f.md")]
    [InlineData("/output/Report/f.md")]
    [InlineData("/output/report./f.md")]
    [InlineData("/output/report /f.md")]
    public void ShouldNeverGrantMoreThanMostSpecificEntry_WhenCaseOrTrailingDotDiffers(string path)
    {
        var scope = Resolve(new FileSystemScopeEntry("/output"), new FileSystemScopeEntry("/output/report", FileAccessRights.ReadOnly));

        Assert.True(scope.Allows(path, FileAccessRights.Read));
        Assert.False(scope.Allows(path, FileAccessRights.Write));
        Assert.True(scope.Allows("/output/other/f.md", FileAccessRights.Write));
    }

    [Fact]
    [Trait("BR", "#349")]
    public void ShouldKeepDeeperWiderEntry_WhenCaseMatches()
    {
        var scope = Resolve(new FileSystemScopeEntry("/output", FileAccessRights.ReadOnly), new FileSystemScopeEntry("/output/notes"));

        Assert.True(scope.Allows("/output/notes/x.md", FileAccessRights.Write));
        Assert.False(scope.Allows("/output/x.md", FileAccessRights.Write));
    }

    [Fact]
    [Trait("BR", "#349")]
    public void ShouldRefuseEntry_WhenAccessGrantsNothing()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Resolve(new FileSystemScopeEntry("/output"), new FileSystemScopeEntry("/output/secret", FileAccessRights.None)));

        Assert.Equal(
            "The file-system scope of agent 'writer': '/output/secret' grants no right; leave the path out of the scope instead.",
            error.Message);
    }
}
