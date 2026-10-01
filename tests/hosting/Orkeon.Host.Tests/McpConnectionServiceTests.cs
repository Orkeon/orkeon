using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Host.Tests.Doubles;
using Orkeon.Hosting;

namespace Orkeon.Host.Tests;

/// <summary>
/// GAP-11 — <c>orkeon-host</c> connects the MCP servers its settings declare, as
/// <c>orkeon run</c> does: their tools are in the host's registry once it has started, before
/// any message can load a crew; a server that cannot be reached costs one error line and the
/// host starts without it.
/// </summary>
public sealed class McpConnectionServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"orkeon-host-mcp-{Guid.NewGuid():N}");

    public McpConnectionServiceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    [Fact]
    public async Task The_servers_tools_are_in_the_registry_once_the_host_has_started()
    {
        await using var server = new FakeHttpMcpServer("search_issues", "create_issue");
        using var host = Build($$"""
            { "MCP": { "Servers": { "tracker": { "Transport": "Sse", "Url": "{{server.Url}}" } } } }
            """);
        var registry = host.Services.GetRequiredService<IToolRegistry>();
        Assert.Null(await registry.GetToolByNameAsync("search_issues"));

        await host.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            Assert.NotNull(await registry.GetToolByNameAsync("search_issues"));
            Assert.NotNull(await registry.GetToolByNameAsync("create_issue"));
        }
        finally
        {
            await host.StopAsync(TestContext.Current.CancellationToken);
        }

        // Stopping the host disconnects the server: its tools leave the registry with it.
        Assert.Null(await registry.GetToolByNameAsync("search_issues"));
    }

    [Fact]
    public async Task An_unreachable_server_costs_one_error_line_and_the_host_starts()
    {
        var logs = new List<string>();
        using var host = Build(
            """
            { "MCP": { "Servers": { "ghost": { "Transport": "Sse", "Url": "http://127.0.0.1:9/mcp/" } } } }
            """,
            logs);

        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.Single(logs, line => line.Contains("MCP server 'ghost' could not be connected", StringComparison.Ordinal));
    }

    [Fact]
    public void The_connection_is_the_first_hosted_service_so_it_precedes_every_message()
    {
        var services = new ServiceCollection();

        services.AddHostLifetimeServices();

        Assert.Equal(
            [typeof(McpConnectionService), typeof(Gateway.ChatChannelService), typeof(CrewHostService)],
            services.Where(d => d.ServiceType == typeof(IHostedService)).Select(d => d.ImplementationType));
    }

    /// <summary>
    /// The runner host the daemon is built on, with only the connection service: the tool
    /// registry constructs every DI tool, and the file tools need a VFS — one internal mount.
    /// </summary>
    private IHost Build(string settingsJson, List<string>? logs = null)
    {
        var settings = Path.Combine(_root, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(settings, settingsJson);

        return RunnerHost.Build(
            settings,
            new RunnerMountPlan { InternalMounts = [$"{FileSystemMount.Quote(_root)}:{RunnerVirtualRoots.Crew}:ro"] },
            configureLogging: logs is null ? null : (_, b) => b.AddProvider(new ErrorSink(logs)),
            configureServices: (_, services) => services.AddHostedService<McpConnectionService>());
    }

    private sealed class ErrorSink(List<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Logger(sink);
        public void Dispose() { }

        private sealed class Logger(List<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                    return;
                lock (sink) { sink.Add(formatter(state, exception)); }
            }
        }
    }
}
