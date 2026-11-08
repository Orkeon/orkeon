using System.Diagnostics;
using SysProcess = System.Diagnostics.Process;
using System.Net.Sockets;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Network;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>
/// FWK-17: the client against the reference server of the MCP project (<c>@modelcontextprotocol/server-everything</c>),
/// over stdio and over Streamable HTTP. It needs <c>npx</c> and the network (or an npm cache that holds the package):
/// skipped locally without <c>npx</c>, failed when <c>CI=true</c> has none.
/// </summary>
[Trait("Category", "Integration")]
public sealed class McpReferenceServerInteropTests
{
    private const string Package = "@modelcontextprotocol/server-everything@2.2.1";
    private static readonly TimeSpan HangGuard = TimeSpan.FromMinutes(3);

    /// <summary>Whether <c>npx</c> answers: a probe of the system, not of the framework.</summary>
    private static bool NpxAvailable()
    {
        // OUT-OF-SCOPE: probing the npx binary on PATH.
        try
        {
            using var process = SysProcess.Start(new ProcessStartInfo("npx", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (process is null)
                return false;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static void RequireNpx()
    {
        var available = NpxAvailable();
        if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
            Assert.True(available, "npx is missing on a CI runner: the reference-server interop cannot run (integration.yml sets up Node).");
        else
            Assert.SkipUnless(available, "npx is not installed: the reference-server interop needs it.");
    }

    private static McpServerConfig StdioConfig()
    {
        var config = new McpServerConfig
        {
            Transport = McpTransportType.Stdio,
            Command = "npx",
            Args = ["-y", "--prefer-offline", Package, "stdio"],
            ConnectTimeout = HangGuard,
        };
        return config;
    }

    private sealed class HttpEverything : IAsyncDisposable
    {
        private readonly SysProcess _process;

        private HttpEverything(SysProcess process, Uri url)
        {
            _process = process;
            Url = url;
        }

        public Uri Url { get; }

        public static async Task<HttpEverything> StartAsync(CancellationToken ct)
        {
            var port = LoopbackPorts.Probe();
            var info = new ProcessStartInfo("npx")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var arg in new[] { "-y", "--prefer-offline", Package, "streamableHttp" })
                info.ArgumentList.Add(arg);
            info.Environment["PORT"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var process = SysProcess.Start(info) ?? throw new InvalidOperationException("npx did not start.");
            _ = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            _ = process.StandardError.ReadToEndAsync(CancellationToken.None);
            var server = new HttpEverything(process, new Uri($"{LoopbackPorts.Host}:{port}/mcp"));

            using var guard = CancellationTokenSource.CreateLinkedTokenSource(ct);
            guard.CancelAfter(HangGuard);
            while (true)
            {
                Assert.False(process.HasExited, "server-everything exited before it listened.");
                try
                {
                    using var probe = new TcpClient();
                    await probe.ConnectAsync("127.0.0.1", port, guard.Token);
                    return server;
                }
                catch (SocketException)
                {
                    await Task.Delay(200, guard.Token);
                }
            }
        }

        public ValueTask DisposeAsync()
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
            _process.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private static async Task ExerciseAsync(McpServerConfig config)
    {
        var registry = new MockToolRegistry();
        await using var provider = new McpToolProvider(registry);
        await provider.ConnectServerAsync("everything", config, TestContext.Current.CancellationToken);

        // Tools: the server's tools are registered, and one is called.
        var tools = (await registry.GetAllToolsAsync()).Select(t => t.Name).ToList();
        Assert.Contains(tools, n => n.Contains("echo", StringComparison.OrdinalIgnoreCase));
        var echo = await CallAsync(registry, "echo", new Dictionary<string, object?> { ["message"] = "interop" });
        Assert.True(echo.Success, echo.Error);
        Assert.Contains("interop", echo.Result?.ToString(), StringComparison.Ordinal);

        // Prompts: listed, and one without arguments is fetched.
        var prompts = await provider.ListPromptsAsync("everything", TestContext.Current.CancellationToken);
        Assert.NotEmpty(prompts);
        var plain = prompts.First(p => p.Arguments.Count == 0);
        var prompt = await provider.GetPromptAsync("everything", plain.Name, null, TestContext.Current.CancellationToken);
        Assert.NotEmpty(prompt.Messages);

        // Resources: listed (the pages are followed; pagination itself is checked against fakes, as the reference
        // server's catalogue fits one page), and one is read.
        var resources = await provider.ListResourcesAsync("everything", TestContext.Current.CancellationToken);
        Assert.NotEmpty(resources);
        var first = resources.First(r => r.Uri is not null);
        var read = await provider.ReadResourceAsync("everything", first.Uri!, TestContext.Current.CancellationToken);
        Assert.NotEmpty(read.Contents);

        // list_changed: the server announces a change of its tool list, and the client lists the tools again.
        await CallAsync(registry, "trigger-list-changed", new Dictionary<string, object?> { ["list"] = "tools" });
        await Polling.WaitUntilAsync(() => Task.FromResult(provider.ToolListActivity("everything").Resyncs >= 1), HangGuard);
        Assert.True(provider.ToolListActivity("everything").Resyncs >= 1, "The client did not list the tools again after tools/list_changed.");

        // resources/updated: a subscribed resource is announced as updated.
        await provider.SubscribeResourceAsync("everything", first.Uri!, TestContext.Current.CancellationToken);
        // A modern subscription takes effect when the stream is opened again with its filter: the trigger is
        // repeated until the server has the subscription, never relying on how long that takes.
        using var guard = new CancellationTokenSource(HangGuard);
        while (provider.ResourceUpdates("everything") == 0 && !guard.IsCancellationRequested)
        {
            await CallAsync(registry, "trigger-resource-update", new Dictionary<string, object?> { ["uri"] = first.Uri!.ToString() });
            await Polling.WaitUntilAsync(() => Task.FromResult(provider.ResourceUpdates("everything") > 0), TimeSpan.FromSeconds(1))
                .ContinueWith(_ => { }, TaskScheduler.Default);
        }

        Assert.True(provider.ResourceUpdates("everything") > 0, "The server sent no resources/updated for a subscribed resource.");
    }

    private static async Task<ToolCallResponse> CallAsync(MockToolRegistry registry, string name, Dictionary<string, object?> arguments)
    {
        var tool = await registry.GetToolByNameAsync(name);
        Assert.NotNull(tool);
        return await tool.CallAsync(new ToolCallRequest(name, arguments), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ShouldServeToolsPromptsAndResources_OverStdio()
    {
        RequireNpx();

        await ExerciseAsync(StdioConfig());
    }

    [Fact]
    public async Task ShouldServeToolsPromptsAndResources_OverStreamableHttp()
    {
        RequireNpx();
        await using var server = await HttpEverything.StartAsync(TestContext.Current.CancellationToken);
        var config = new McpServerConfig
        {
            Transport = McpTransportType.Sse,
            Url = server.Url,
            ConnectTimeout = HangGuard,
        };

        await ExerciseAsync(config);
    }
}
