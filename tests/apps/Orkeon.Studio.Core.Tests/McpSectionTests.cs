using Orkeon.Studio.Core.Configuration;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// STUDIO-21 — the typed view over <c>MCP</c>: servers keyed by identifier, every write in
/// place, keys Studio does not model kept whole, and the runtime's spelling for the fields.
/// </summary>
public sealed class McpSectionTests
{
    [Fact]
    public void A_server_round_trips_its_five_fields_under_its_identifier()
    {
        var document = AppSettingsDocument.CreateEmpty();

        document.Mcp.SetServer(new McpServerDefinition
        {
            Id = "files",
            Command = "npx",
            Args = ["-y", "@modelcontextprotocol/server-filesystem", "/data"],
            Env = new Dictionary<string, string>(StringComparer.Ordinal) { ["NODE_ENV"] = "production" },
        });

        var reparsed = AppSettingsDocument.Parse(document.ToJson());
        var server = Assert.IsType<McpServerDefinition>(reparsed.Mcp.GetServer("files"));
        Assert.Equal(["files"], reparsed.Mcp.ServerIds);
        Assert.True(server.IsStdio);
        Assert.Equal("Stdio", reparsed.GetString("MCP:Servers:files:Transport"));
        Assert.Equal("npx", server.Command);
        Assert.Equal(["-y", "@modelcontextprotocol/server-filesystem", "/data"], server.Args);
        Assert.Equal("production", server.Env["NODE_ENV"]);
        Assert.Null(server.Url);
        Assert.True(reparsed.Mcp.HasServers);
        Assert.True(reparsed.Mcp.IsEnabled);
        Assert.Null(reparsed.Mcp.Enabled);
    }

    [Fact]
    public void An_http_server_keeps_its_url_and_writes_no_empty_block()
    {
        var document = AppSettingsDocument.CreateEmpty();

        document.Mcp.SetServer(new McpServerDefinition
        {
            Id = "remote",
            Transport = McpSection.SseTransport,
            Url = "https://mcp.example.com/rpc",
        });

        var json = document.ToJson();
        Assert.Contains("\"Url\": \"https://mcp.example.com/rpc\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Args\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Env\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Command\"", json, StringComparison.Ordinal);
        Assert.True(document.Mcp.GetServer("remote")!.IsSse);
    }

    [Fact]
    public void Editing_a_server_keeps_the_keys_studio_does_not_model()
    {
        var document = AppSettingsDocument.Parse("""
            { "MCP": { "Enabled": true, "Servers": { "files": { "Command": "old", "Timeout": 30, "Nested": { "x": 1 } } } }, "Llm": { "Model": "m" } }
            """);

        var server = document.Mcp.GetServer("files")!;
        document.Mcp.SetServer(server with { Command = "new", Args = ["--stdio"] });

        Assert.Equal(30, document.GetInt32("MCP:Servers:files:Timeout"));
        Assert.Equal(1, document.GetInt32("MCP:Servers:files:Nested:x"));
        Assert.Equal("new", document.GetString("MCP:Servers:files:Command"));
        Assert.Equal("m", document.Llm.Model);
        Assert.True(document.Mcp.Enabled);
    }

    [Fact]
    public void Renaming_moves_the_whole_node_and_removing_the_last_server_drops_the_dictionary()
    {
        var document = AppSettingsDocument.Parse("""
            { "MCP": { "Servers": { "a": { "Command": "one", "Timeout": 5 }, "b": { "Command": "two" } } } }
            """);

        document.Mcp.RenameServer("a", "alpha");

        Assert.Equal(["b", "alpha"], document.Mcp.ServerIds);
        Assert.Equal(5, document.GetInt32("MCP:Servers:alpha:Timeout"));
        Assert.Null(document.GetNode("MCP:Servers:a"));

        document.Mcp.RemoveServer("b");
        document.Mcp.RemoveServer("alpha");

        Assert.Empty(document.Mcp.ServerIds);
        Assert.Null(document.GetNode("MCP:Servers"));
        Assert.False(document.Mcp.HasServers);
    }

    [Theory]
    [InlineData("files", true)]
    [InlineData("my-server.v2_x", true)]
    [InlineData("", false)]
    [InlineData("a:b", false)]
    [InlineData("with space", false)]
    public void An_identifier_is_one_the_binder_keeps_whole(string id, bool valid)
    {
        Assert.Equal(valid, McpSection.IsValidServerId(id));
        if (!valid)
        {
            var document = AppSettingsDocument.CreateEmpty();
            Assert.Throws<ArgumentException>(() => document.Mcp.SetServer(new McpServerDefinition { Id = id }));
        }
    }

    [Fact]
    public void The_switch_and_an_empty_environment_are_written_the_way_the_runtime_reads_them()
    {
        var document = AppSettingsDocument.CreateEmpty();
        document.Mcp.Enabled = false;
        document.Mcp.SetServer(new McpServerDefinition
        {
            Id = "s",
            Command = "cmd",
            Env = new Dictionary<string, string>(StringComparer.Ordinal) { ["A"] = "1" },
        });
        document.Mcp.SetServer(document.Mcp.GetServer("s")! with { Env = new Dictionary<string, string>(StringComparer.Ordinal) });

        Assert.False(document.Mcp.IsEnabled);
        Assert.Null(document.GetNode("MCP:Servers:s:Env"));
        Assert.Empty(document.Mcp.GetServer("s")!.Env);
    }
}
