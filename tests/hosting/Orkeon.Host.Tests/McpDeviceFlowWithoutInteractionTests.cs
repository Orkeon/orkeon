using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Host.Tests.Doubles;
using Orkeon.Hosting;
using Orkeon.Tools.Abstractions.Auth;

namespace Orkeon.Host.Tests;

/// <summary>
/// FWK-16: <c>orkeon-host</c> registers no <see cref="IOAuthLoginInteraction"/>, so a server in
/// <c>oauth2-device</c> has nobody to show its code to: it is set aside at start, one error line names it.
/// </summary>
public sealed class McpDeviceFlowWithoutInteractionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"orkeon-host-mcp-oauth-{Guid.NewGuid():N}");

    public McpDeviceFlowWithoutInteractionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    [Fact]
    public async Task A_device_flow_server_is_set_aside_naming_it_while_the_host_starts()
    {
        await using var server = new FakeHttpMcpServer("search_issues");
        var logs = new List<string>();
        using var host = Build($$"""
            { "MCP": { "Servers": { "tracker": { "Transport": "Sse", "Url": "{{server.Url}}",
              "Auth": { "Type": "oauth2-device", "TokenUrl": "https://idp.example.test/token",
                        "DeviceAuthorizationUrl": "https://idp.example.test/device", "ClientId": "orkeon-cli" } } } } }
            """, logs);
        Assert.Null(host.Services.GetService<IOAuthLoginInteraction>());

        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        var line = Assert.Single(logs, l => l.Contains("MCP server 'tracker' could not be connected", StringComparison.Ordinal));
        Assert.Contains("oauth2-device", line, StringComparison.Ordinal);
        Assert.Null(await host.Services.GetRequiredService<IToolRegistry>().GetToolByNameAsync("search_issues"));
    }

    private IHost Build(string settingsJson, List<string> logs)
    {
        var settings = Path.Combine(_root, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(settings, settingsJson);

        return RunnerHost.Build(
            settings,
            new RunnerMountPlan { InternalMounts = [$"{FileSystemMount.Quote(_root)}:{RunnerVirtualRoots.Crew}:ro"] },
            configureLogging: (_, b) => b.AddProvider(new ErrorSink(logs)),
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
