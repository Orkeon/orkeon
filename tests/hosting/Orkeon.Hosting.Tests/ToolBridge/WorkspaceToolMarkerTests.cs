using Orkeon.Domain.Tools;
using Orkeon.Hosting.ToolBridge;

namespace Orkeon.Hosting.Tests.ToolBridge;

/// <summary>
/// #445 - the built-in tools of the files, the shell and the code index say they act on the workspace, and the tools of the caller's own
/// run say they stay with it, so the lone '*' of a bridge never selects them. Read on the tools a run registers; joins <see cref="ConsoleSerialCollection"/>
/// because the host build writes on the console.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class WorkspaceToolMarkerTests : IDisposable
{
    private static readonly string[] Workspace =
    [
        "file_read", "file_write", "edit_file", "glob", "grep", "count_pattern", "directory_read", "directory_search",
        "list_mounts", "shell_command", "code_interpreter",
        "codebase_search", "codebase_map", "index_codebase", "symbol_source", "node_replace", "edit_batch", "structural_rewrite",
    ];

    private static readonly string[] CallersOwn =
    [
        "chat_attach", "human_input", "memory_store", "memory_recall", "session_cost", "session_snip", "session_stats", "session_store",
        "token_budget", "spawn_agent", "system_one_decide",
    ];

    private readonly string _root = RegisteredTools.CreateRoot("orkeon-tool-marker-");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    [Fact]
    public async Task ShouldMarkEveryWorkspaceTool_WhenTheHostRegistersIt()
    {
        // Arrange
        var tools = await RegisteredTools.ReadAsync(_root);

        // Act
        var unmarked = tools.Where(t => Workspace.Contains(t.Name, StringComparer.Ordinal) && !ToolContracts.ActsOnWorkspace(t))
            .Select(t => t.Name).ToList();

        // Assert
        Assert.True(tools.Count(t => Workspace.Contains(t.Name, StringComparer.Ordinal)) > 5, "Too few workspace tools were read from the host.");
        Assert.Empty(unmarked);
    }

    [Fact]
    public async Task ShouldLeaveTheBrowserUnmarked_WhenTheHostRegistersIt()
    {
        // Arrange
        var tools = await RegisteredTools.ReadAsync(_root);

        // Act
        var marked = tools.Where(t => t.Name is "firefox" or "web_fetch" or "web_search" && ToolContracts.ActsOnWorkspace(t))
            .Select(t => t.Name).ToList();

        // Assert
        Assert.Empty(marked);
    }

    [Fact]
    public async Task ShouldMarkEveryToolOfTheCallersRun_WhenTheHostRegistersIt()
    {
        // Arrange
        var tools = await RegisteredTools.ReadAsync(_root);

        // Act
        var unmarked = tools.Where(t => CallersOwn.Contains(t.Name, StringComparer.Ordinal) && !ToolContracts.StaysWithCaller(t))
            .Select(t => t.Name).ToList();

        // Assert
        Assert.True(tools.Count(t => CallersOwn.Contains(t.Name, StringComparer.Ordinal)) > 5, "Too few tools of the caller's run were read from the host.");
        Assert.Empty(unmarked);
    }

    [Fact]
    public async Task ShouldLetTheLoneStarSelectTheBrowser_WhenTheHostRegistersIt()
    {
        // Arrange
        var tools = await RegisteredTools.ReadAsync(_root);

        // Act
        var result = ToolBridgeSelection.Resolve("Orkeon:Bridge", tools, ["*"], []);
        var names = result.Tools.Select(t => t.Name).ToList();

        // Assert
        Assert.NotEmpty(names);
        Assert.DoesNotContain(names, n => CallersOwn.Contains(n, StringComparer.Ordinal) || Workspace.Contains(n, StringComparer.Ordinal));
    }
}
