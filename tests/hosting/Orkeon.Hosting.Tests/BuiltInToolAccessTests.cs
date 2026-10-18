using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.Security;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// #126 - the access every built-in tool declares, read on the tools a run registers (the host
/// <c>--list-tools</c> builds). A tool left at <see cref="ToolAccess.Unspecified"/> is classified by
/// the gate's name tables, or as a write when they do not know it: every call then asks in
/// <c>default</c> and is refused in <c>plan</c>. The tools still in that case are pinned here, so a
/// new tool cannot join them unnoticed and a tool that gains a declaration leaves the list.
/// Joins <see cref="ConsoleSerialCollection"/> because the host build writes on the console.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class BuiltInToolAccessTests : IDisposable
{
    /// <summary>The built-in tools a run registers (<see cref="RegisteredTools"/>) that still declare no access.</summary>
    private static readonly string[] StillUnspecified =
    [
        "arcadedb_query", "cache_search", "csv_reader", "database_schema", "docx_reader", "docx_writer",
        "get_last_value", "github", "graph_schema", "human_input", "image_generation", "janusgraph_query",
        "json_tool", "mariadb_query", "mdx_search", "mongodb_query", "mongodb_schema", "mysql_query",
        "pdf_reader", "pdf_search", "post_message", "postgres_query", "publish_event", "rag_eval",
        "receive_message", "relational_database_query", "reply_to", "send_request", "sqlserver_query",
        "txt_search", "wait_for_event", "web_search", "xlsx_reader", "xlsx_writer", "xml_parser",
    ];

    private static readonly Dictionary<string, object?> NoArgs = new();

    private readonly string _root;

    public BuiltInToolAccessTests()
    {
        _root = RegisteredTools.CreateRoot("orkeon-tool-access-");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    [Fact]
    public async Task ShouldListEveryRegisteredToolLeftAtUnspecified()
    {
        var tools = await RegisteredToolsAsync();

        var unspecified = tools
            .Where(t => t.Access == ToolAccess.Unspecified)
            .Select(t => t.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(StillUnspecified.Order(StringComparer.Ordinal).SequenceEqual(unspecified), "Left at Unspecified: " + string.Join(", ", unspecified));
    }

    [Theory]
    [InlineData("web_scrape")]
    [InlineData("scrape_element")]
    [InlineData("rag_search")]
    public async Task ShouldAllowTheLookupToolsInPlanAndDefaultWithoutAQuestion(string name)
    {
        var tool = Assert.Single(await RegisteredToolsAsync(), t => t.Name == name);
        var gate = new ModePermissionGate(isInteractive: true);

        Assert.Equal(ToolAccess.Read, tool.Access);
        Assert.Equal(PermissionAction.Allow, (await gate.CheckAsync(name, NoArgs, "plan", tool.Access, TestContext.Current.CancellationToken)).Action);
        Assert.Equal(PermissionAction.Allow, (await gate.CheckAsync(name, NoArgs, "default", tool.Access, TestContext.Current.CancellationToken)).Action);
    }

    [Fact]
    public async Task ShouldClassifyRagIngestByItsDeclaredAccess()
    {
        var tool = Assert.Single(await RegisteredToolsAsync(), t => t.Name == "rag_ingest");
        var gate = new ModePermissionGate(isInteractive: true);

        Assert.Equal(ToolAccess.Edit, tool.Access);
        Assert.Equal(PermissionAction.Allow, (await gate.CheckAsync("rag_ingest", NoArgs, "acceptEdits", tool.Access, TestContext.Current.CancellationToken)).Action);
        Assert.Equal(PermissionAction.Ask, (await gate.CheckAsync("rag_ingest", NoArgs, "default", tool.Access, TestContext.Current.CancellationToken)).Action);
        Assert.Equal(PermissionAction.Deny, (await gate.CheckAsync("rag_ingest", NoArgs, "plan", tool.Access, TestContext.Current.CancellationToken)).Action);
    }

    private Task<IReadOnlyList<IBaseTool>> RegisteredToolsAsync() => RegisteredTools.ReadAsync(_root);
}
