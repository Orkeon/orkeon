using Jint;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.ConsoleApp.Tests.Fakes;
using Orkeon.Domain.Tools;
using Orkeon.Hosting;
using Orkeon.Scripting;

namespace Orkeon.ConsoleApp.Tests.DependencyInjection;

/// <summary>
/// The REPL reaches the tools an MCP server or a plugin contributes, as <c>orkeon run</c> reaches an MCP
/// server's: the servers of <c>MCP:Servers</c> are connected before the commands load, the plugins of the
/// <c>Plugins</c> section are loaded with the container, and the scripts' catalogue — the <c>tools</c>
/// namespace, an agent's <c>.tools([...])</c>, <c>act</c> — holds their tools beside the built-in ones.
/// </summary>
public sealed class ReplMcpAndPluginsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("orkeon-repl-mcp-").FullName;
    private readonly List<string> _reported = [];

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
        catch (UnauthorizedAccessException) { /* a plugin assembly stays loaded: best-effort */ }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Microsoft.Extensions.Hosting.IHost Start(string json)
    {
        var settings = Path.Combine(_root, "repl.json");
        File.WriteAllText(settings, json);
        var launch = ReplStartup.Prepare(
            ["--settings", settings, "--mount", $"{_root}:/workspace:ro"], UiMode.Plain, replWordWrap: false, _reported.Add);
        Assert.True(launch.Host is not null, string.Join(Environment.NewLine, _reported));
        return launch.Host;
    }

    private static string Json(string value) => System.Text.Json.JsonSerializer.Serialize(value);

    private static bool NodeIsOnPath() =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => File.Exists(Path.Combine(directory, OperatingSystem.IsWindows() ? "node.exe" : "node")));

    [Fact]
    public async Task An_MCP_servers_tool_reaches_the_tools_namespace_of_the_scripts()
    {
        Assert.SkipUnless(NodeIsOnPath(), "The stdio MCP server of this test runs on node, which is not on PATH.");
        var server = Path.Combine(AppContext.BaseDirectory, "Fixtures", "mcp-echo-server.mjs");
        using var host = Start($$"""
            { "MCP": { "Servers": { "echo": { "Command": "node", "Args": [ {{Json(server)}} ] } } } }
            """);

        await McpStartup.ConnectConfiguredServersAsync(host.Services, Ct);
        using var engine = host.Services.GetRequiredService<JsEngineFactory>().Create();

        Assert.True((await engine.EvaluateAsync("Object.keys(tools).includes('echoText')", cancellationToken: Ct)).AsBoolean());
        var echoed = await (await engine.EvaluateAsync("tools.echoText({ text: 'from the script' })", cancellationToken: Ct))
            .UnwrapIfPromiseAsync(Ct);
        Assert.Contains("from the script", echoed.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_plugins_tool_reaches_the_tools_namespace_of_the_scripts()
    {
        var plugins = Directory.CreateDirectory(Path.Combine(_root, "plugins")).FullName;
        File.Copy(typeof(FakeEchoPlugin).Assembly.Location, Path.Combine(plugins, "EchoPlugin.dll"));
        using var host = Start($$"""
            {
              "Orkeon": { "FileSystem": { "InternalMounts": [ {{Json($"{plugins}:/plugins:ro")}} ] } },
              "Plugins": { "Directory": "/plugins" }
            }
            """);

        using var engine = host.Services.GetRequiredService<JsEngineFactory>().Create();

        Assert.True((await engine.EvaluateAsync("Object.keys(tools).includes('pluginEcho')", cancellationToken: Ct)).AsBoolean());
        var echoed = await (await engine.EvaluateAsync("tools.pluginEcho({ text: 'from a plugin' })", cancellationToken: Ct))
            .UnwrapIfPromiseAsync(Ct);
        Assert.Equal("from a plugin", echoed.ToString());
    }

    [Fact]
    public void Without_a_Plugins_section_no_plugin_is_loaded()
    {
        using var host = Start("""{ }""");

        Assert.Null(host.Services.GetService<Orkeon.Plugins.IPluginRegistry>());
        Assert.DoesNotContain(
            host.Services.GetRequiredService<JsEngineFactory>().Create().Evaluate("Object.keys(tools)").AsArray(),
            name => name.ToString() == "pluginEcho");
    }

    [Fact]
    public async Task A_tool_the_registry_gains_after_the_start_reaches_the_next_engine()
    {
        using var host = Start("""{ }""");
        var factory = host.Services.GetRequiredService<JsEngineFactory>();
        using var before = factory.Create();

        await host.Services.GetRequiredService<IToolRegistry>()
            .RegisterToolAsync(new Orkeon.Tests.Shared.Doubles.StubBaseTool("late_tool"));
        using var after = factory.Create();

        Assert.False((await before.EvaluateAsync("Object.keys(tools).includes('lateTool')", cancellationToken: Ct)).AsBoolean());
        Assert.True((await after.EvaluateAsync("Object.keys(tools).includes('lateTool')", cancellationToken: Ct)).AsBoolean());
    }
}
