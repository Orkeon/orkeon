using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Infrastructure.MCP;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// STUDIO-21 — the shared runner host honours the <c>MCP</c> section of the settings: the
/// provider exists only when servers are declared and MCP is not switched off, and a server
/// that cannot be connected is reported and skipped rather than ending the run.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerHostMcpTests : IDisposable
{
    private readonly string _root;

    public RunnerHostMcpTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "orkeon-mcp-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    [Fact]
    public void Without_a_section_the_provider_is_not_registered()
    {
        using var host = RunnerHost.Build(settingsPath: null, mounts: new RunnerMountPlan());

        Assert.Null(host.Services.GetService<McpToolProvider>());
    }

    [Fact]
    public void Declared_servers_register_the_provider_and_the_switch_removes_it()
    {
        var declared = Settings("""
            { "MCP": { "Servers": { "demo": { "Command": "demo-mcp", "Args": ["--stdio"] } } } }
            """);
        var switchedOff = Settings("""
            { "MCP": { "Enabled": false, "Servers": { "demo": { "Command": "demo-mcp" } } } }
            """);
        var empty = Settings("""{ "MCP": { "Enabled": true, "Servers": { } } }""");

        using (var host = RunnerHost.Build(declared, Mounts()))
            Assert.NotNull(host.Services.GetService<McpToolProvider>());
        using (var host = RunnerHost.Build(switchedOff, Mounts()))
            Assert.Null(host.Services.GetService<McpToolProvider>());
        using (var host = RunnerHost.Build(empty, Mounts()))
            Assert.Null(host.Services.GetService<McpToolProvider>());
    }

    [Fact]
    public async Task A_server_that_cannot_start_is_reported_and_the_run_goes_on()
    {
        var settings = Settings("""
            { "MCP": { "Servers": { "ghost": { "Command": "orkeon-no-such-mcp-server-3f9c1", "Args": ["--stdio"] } } } }
            """);
        using var logs = new CapturingLoggerProvider();
        using var stderr = new StringWriter();
        var original = Console.Error;
        Console.SetError(stderr);
        try
        {
            using var host = RunnerHost.Build(
                settings,
                Mounts(),
                configureLogging: (_, b) => b.AddProvider(logs));
            var registry = host.Services.GetRequiredService<IToolRegistry>();
            var before = (await registry.GetAllToolsAsync()).Count;

            await McpStartup.ConnectConfiguredServersAsync(host, CancellationToken.None);

            var after = (await registry.GetAllToolsAsync()).Count;
            Assert.Equal(before, after);
            var error = Assert.Single(logs.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("'ghost'", StringComparison.Ordinal));
            Assert.Contains("could not be connected", error.Message, StringComparison.Ordinal);
            Assert.Contains("MCP server 'ghost' could not be connected", stderr.ToString(), StringComparison.Ordinal);
            // Disposing the host synchronously must not throw on the async-only provider.
        }
        finally
        {
            Console.SetError(original);
        }
    }

    /// <summary>
    /// The provider pulls the tool registry, which constructs every DI tool, and the file tools
    /// need a VFS: one internal mount of the temp root, exactly what --list-tools gives itself.
    /// </summary>
    private RunnerMountPlan Mounts() => new()
    {
        InternalMounts = [$"{FileSystemMount.Quote(_root)}:{RunnerVirtualRoots.Crew}:ro"],
    };

    private string Settings(string json)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        return path;
    }
}
