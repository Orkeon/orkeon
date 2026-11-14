using Orkeon.Application.Agent;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Application.Tests.Agent;

/// <summary>#378: the <c>## File system</c> section, the one text a declarative turn and an agent body both read.</summary>
public sealed class AgentFileSystemPromptTests
{
    private static MountInfo Mount(string path, FileAccessRights rights, params SubPathOverride[] overrides)
        => new(path, rights, overrides);

    private static string Lines(params string[] lines) => string.Join(Environment.NewLine, lines);

    [Fact]
    [Trait("BR", "#378")]
    public void ShouldRenderExactSection_WhenMountsHaveOverrides()
    {
        // Arrange: declared out of order, overrides out of order, so the sort is proven.
        IReadOnlyList<MountInfo> mounts =
        [
            Mount("/workspace", FileAccessRights.ReadOnly,
                new SubPathOverride("secrets", FileAccessRights.None),
                new SubPathOverride("/notes/", FileAccessRights.ReadWriteNoDelete)),
            Mount("/docs", FileAccessRights.ReadOnly),
        ];

        // Act
        var section = AgentFileSystemPrompt.Render(mounts, scope: null, "/workspace", isRoot: false);

        // Assert
        Assert.Equal(Lines(
            "## File system",
            "",
            "Only these virtual paths exist; any other path is refused:",
            "- /docs (read-only)",
            "- /workspace (read-only)",
            "  - /workspace/notes (read-write, no delete)",
            "  - /workspace/secrets (no access)",
            "Relative paths resolve against /workspace.",
            "Write intermediate notes under a read-write path."), section);
    }

    [Fact]
    [Trait("BR", "#378")]
    public void ShouldOmitWriteNote_WhenEveryPathIsReadOnly()
    {
        IReadOnlyList<MountInfo> mounts = [Mount("/docs", FileAccessRights.ReadOnly)];

        var section = AgentFileSystemPrompt.Render(mounts, scope: null, workingDirectory: null, isRoot: false);

        Assert.Equal(Lines(
            "## File system",
            "",
            "Only these virtual paths exist; any other path is refused:",
            "- /docs (read-only)"), section);
    }

    [Fact]
    [Trait("BR", "#378")]
    public void ShouldBeEmpty_WhenNoMountIsVisible()
    {
        IReadOnlyList<MountInfo> mounts = [Mount("/workspace", FileAccessRights.ReadWrite)];
        var scope = AgentFileSystemScope.Resolve("a", [new FileSystemScopeEntry("/workspace")], mounts);

        Assert.Equal("", AgentFileSystemPrompt.Render([], scope: null, "/workspace", isRoot: false));
        Assert.Equal("", AgentFileSystemPrompt.Render([], scope, "/workspace", isRoot: true));
    }

    [Fact]
    [Trait("BR", "#378")]
    public void ShouldSayOutsideScope_WhenScopedWorkingDirectoryIsOutsideScope()
    {
        IReadOnlyList<MountInfo> mounts = [Mount("/workspace", FileAccessRights.ReadWrite)];
        var scope = AgentFileSystemScope.Resolve("a", [new FileSystemScopeEntry("/workspace/src")], mounts);

        var section = AgentFileSystemPrompt.Render(mounts, scope, "/workspace", isRoot: false);

        Assert.Equal(Lines(
            "## File system",
            "",
            "Only these virtual paths exist; any other path is refused:",
            "- /workspace/src (read-write)",
            "Relative paths resolve against /workspace, outside your file-system scope: write every path in full, under one of the paths above.",
            "Write intermediate notes under a read-write path."), section);
    }

    [Fact]
    [Trait("BR", "#378")]
    public void ShouldNameWorkingRoot_WhenIsRoot()
    {
        IReadOnlyList<MountInfo> mounts = [Mount("/workspace", FileAccessRights.ReadWrite)];

        var section = AgentFileSystemPrompt.Render(mounts, scope: null, "/workspace/src", isRoot: true);

        Assert.Equal(Lines(
            "## File system",
            "",
            "Only these virtual paths exist; any other path is refused:",
            "- /workspace (read-write)",
            "Your working root is /workspace/src: relative paths resolve there.",
            "Write intermediate notes under a read-write path."), section);
    }
}
