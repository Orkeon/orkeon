using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Infrastructure.MCP;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// The shared runner host honours the <c>MCP</c> section of the settings: the
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

    /// <summary>
    /// GAP-24: no setting starts an MCP server. A settings file that writes <c>MCP:EnableServer</c> —
    /// servers declared or not, true or false — is refused at startup as a key the section does not
    /// carry, never ignored.
    /// </summary>
    [Theory]
    [InlineData("""{ "MCP": { "EnableServer": true } }""")]
    [InlineData("""{ "MCP": { "EnableServer": false, "Servers": { "demo": { "Command": "demo-mcp" } } } }""")]
    public void A_settings_file_that_writes_EnableServer_is_refused_as_an_unknown_key(string json)
    {
        var settings = Settings(json);

        var ex = Assert.Throws<RunnerSettingsException>(() => RunnerHost.Build(settings, Mounts()));

        Assert.Contains("MCP:EnableServer is not a setting", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// FWK-16: an <c>Auth:Type</c> that is not one of the listed spellings sets the server aside with a line that
    /// names it, the key and the accepted spellings: the binder alone would drop the server without a word.
    /// </summary>
    [Theory]
    [InlineData("OAUTH2DEVICE")]
    [InlineData("o-auth2-device")]
    [InlineData("3")]
    public async Task An_unlisted_Auth_Type_sets_the_server_aside_and_lists_the_accepted_spellings(string type)
    {
        var settings = Settings($$"""
            { "MCP": { "Servers": { "tracker": { "Transport": "Sse", "Url": "https://mcp.example.test/", "Auth": { "Type": "{{type}}" } } } } }
            """);
        using var logs = new CapturingLoggerProvider();
        using var stderr = new StringWriter();
        var original = Console.Error;
        Console.SetError(stderr);
        try
        {
            using var host = RunnerHost.Build(settings, Mounts(), configureLogging: (_, b) => b.AddProvider(logs));

            await McpStartup.ConnectConfiguredServersAsync(host, CancellationToken.None);

            var error = Assert.Single(logs.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("'tracker'", StringComparison.Ordinal));
            Assert.Contains("MCP:Servers:tracker", error.Message, StringComparison.Ordinal);
            Assert.Contains($"'{type}' is not an MCP Auth:Type", error.Message, StringComparison.Ordinal);
            Assert.Contains("oauth2-device", error.Message, StringComparison.Ordinal);
            Assert.Contains("oauth2-client-credentials", stderr.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(original);
        }
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
