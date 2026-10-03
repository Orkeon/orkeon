using System.Text.Json;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// GAP-24 — <c>orkeon mcp serve</c> driven the way an MCP client drives it: JSON-RPC messages
/// written to its stdin, one per line, the answers read back from its stdout, which carries
/// nothing else. The host is built for real over a settings file in a scratch directory, the
/// way <c>orkeon run --list-tools</c> builds it. Until this verb, no shipped binary started the
/// server: <c>MCP:EnableServer</c> registered it, and nothing ever resolved it.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class McpServeCommandTests
{
    private const string Discover =
        """{"jsonrpc":"2.0","id":1,"method":"server/discover","params":{"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28"}}}""";

    private const string ListTools = """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""";

    [Fact]
    public async Task Serve_answers_discover_then_lists_the_tools_of_the_host_a_run_builds()
    {
        using var scratch = new ScriptScratch();
        var settings = scratch.WriteFile("appsettings.json", "{}");

        IReadOnlyList<string> manifest;
        using (var listing = new TestConsole())
        {
            Assert.Equal(Program.ExitOk, await Program.DispatchAsync(["run", "--list-tools", "--settings", settings]));
            manifest = Lines(listing.Stdout);
        }

        using var console = new TestConsole(stdin: Discover + "\n" + ListTools + "\n");

        var exit = await Program.DispatchAsync(["mcp", "serve", "--settings", settings]);

        Assert.Equal(Program.ExitOk, exit);
        var answers = Answers(console.Stdout);
        Assert.Equal(2, answers.Count);
        Assert.Equal(1, answers[0].GetProperty("id").GetInt32());
        Assert.Contains("2026-07-28", answers[0].GetProperty("result").GetProperty("supportedVersions")
            .EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(2, answers[1].GetProperty("id").GetInt32());
        // Every tool of the run's host but human_input, which answers for the operator of a run:
        // the client of an MCP server has a human of its own.
        Assert.Contains("file_read", manifest);
        Assert.Contains("human_input", manifest);
        Assert.Equal(manifest.Where(name => name != "human_input"), ToolNames(answers[1]));
    }

    [Fact]
    public async Task Tools_exposes_the_named_tools_only()
    {
        using var scratch = new ScriptScratch();
        var settings = scratch.WriteFile("appsettings.json", "{}");
        using var console = new TestConsole(stdin: ListTools + "\n");

        var exit = await Program.DispatchAsync(["mcp", "serve", "--settings", settings, "--tools", "rag_search,file_read"]);

        Assert.Equal(Program.ExitOk, exit);
        var answer = Assert.Single(Answers(console.Stdout));
        Assert.Equal(["file_read", "rag_search"], ToolNames(answer));
    }

    [Fact]
    public async Task A_tool_the_host_does_not_have_refuses_the_start_and_is_named()
    {
        using var scratch = new ScriptScratch();
        var settings = scratch.WriteFile("appsettings.json", "{}");
        using var console = new TestConsole(stdin: ListTools + "\n");

        var exit = await Program.DispatchAsync(["mcp", "serve", "--settings", settings, "--tools", "file_read,no_such_tool"]);

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("no_such_tool", console.Stderr, StringComparison.Ordinal);
        Assert.Empty(console.Stdout);
    }

    [Fact]
    public async Task A_call_crosses_the_guard_and_reaches_the_audit_trail()
    {
        using var scratch = new ScriptScratch();
        var settings = scratch.WriteFile("appsettings.json", "{}");
        const string call =
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"file_read","arguments":{"path":"../../etc/passwd"}}}""";
        using var console = new TestConsole(stdin: call + "\n");

        var exit = await Program.DispatchAsync(["mcp", "serve", "--settings", settings]);

        Assert.Equal(Program.ExitOk, exit);
        var result = Assert.Single(Answers(console.Stdout)).GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Contains("Blocked by Guardian", result.GetProperty("content")[0].GetProperty("text").GetString(), StringComparison.Ordinal);
        // The audit trail's log sink, on stderr: the call is recorded, under the caller "mcp".
        Assert.Contains("Tool execution: file_read -> Blocked", console.Stderr, StringComparison.Ordinal);
        Assert.Contains("Agent=mcp", console.Stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// An internal mount walls off every path under its folder (<c>FileSystemRegistry</c>): the
    /// working directory, mounted internally so the file tools can be built at all, must not
    /// stand over a settings mount it contains — an MCP client may well start the server in
    /// <c>/</c>.
    /// </summary>
    [Fact]
    public async Task A_settings_mount_under_the_working_directory_reaches_the_file_tools()
    {
        using var scratch = new ScriptScratch();
        var docs = Path.Combine(scratch.Root, "docs");
        Directory.CreateDirectory(docs);
        await File.WriteAllTextAsync(Path.Combine(docs, "note.txt"), "hello from the mount", TestContext.Current.CancellationToken);
        var mount = JsonSerializer.Serialize($"{docs}:/docs:ro");
        var settings = scratch.WriteFile("appsettings.json", $$"""{ "Orkeon": { "FileSystem": { "Mounts": [ {{mount}} ] } } }""");
        const string read =
            """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"file_read","arguments":{"path":"/docs/note.txt"}}}""";
        using var console = new TestConsole(stdin: read + "\n");

        var exit = await Orkeon.Scripting.Cli.Commands.McpCommand.ServeAsync(new Orkeon.Scripting.Cli.Commands.McpServeCommandOptions
        {
            SettingsPath = settings,
            WorkingDirectoryOverride = scratch.Root,
        });

        Assert.Equal(Program.ExitOk, exit);
        var result = Assert.Single(Answers(console.Stdout)).GetProperty("result");
        var text = result.GetProperty("content")[0].GetProperty("text").GetString();
        Assert.False(result.GetProperty("isError").GetBoolean(), text);
        Assert.Contains("hello from the mount", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_settings_file_that_still_enables_the_server_is_refused_naming_the_verb()
    {
        using var scratch = new ScriptScratch();
        var settings = scratch.WriteFile("appsettings.json", """{ "MCP": { "EnableServer": true } }""");
        using var console = new TestConsole(stdin: ListTools + "\n");

        var exit = await Program.DispatchAsync(["mcp", "serve", "--settings", settings]);

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("MCP:EnableServer", console.Stderr, StringComparison.Ordinal);
        Assert.Contains("orkeon mcp serve", console.Stderr, StringComparison.Ordinal);
        Assert.Empty(console.Stdout);
    }

    [Fact]
    public async Task Help_names_the_options_on_stderr_and_leaves_stdout_to_the_protocol()
    {
        using var console = new TestConsole();

        var exit = await Program.DispatchAsync(["mcp", "serve", "--help"]);

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains("--settings", console.Stderr, StringComparison.Ordinal);
        Assert.Contains("--tools", console.Stderr, StringComparison.Ordinal);
        Assert.Empty(console.Stdout);
    }

    private static List<string> Lines(string text) =>
        text.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0).ToList();

    /// <summary>Every line of stdout, each of which must be a JSON-RPC message.</summary>
    private static List<JsonElement> Answers(string stdout) =>
        Lines(stdout).Select(line =>
        {
            using var document = JsonDocument.Parse(line);
            Assert.Equal("2.0", document.RootElement.GetProperty("jsonrpc").GetString());
            return document.RootElement.Clone();
        }).ToList();

    private static List<string?> ToolNames(JsonElement answer) =>
        answer.GetProperty("result").GetProperty("tools").EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString())
            .ToList();
}
