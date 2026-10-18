using System.Diagnostics;
using System.Text.Json;
using Orkeon.Infrastructure.MCP;
using SysProcess = System.Diagnostics.Process;
using StdioMcpTransportSut = Orkeon.Infrastructure.MCP.StdioMcpTransport;

namespace Orkeon.Infrastructure.Tests.MCP;

public class StdioMcpTransportTests
{
    /// <summary>
    /// Creates a ProcessStartInfo for an echo process (reads stdin, writes to stdout line by line).
    /// Uses "cat" on Linux/macOS and PowerShell with explicit flush on Windows
    /// (findstr buffers output on redirected pipes).
    /// </summary>
    private static ProcessStartInfo CreateEchoProcessStartInfo()
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -Command \"while($null -ne ($l=[Console]::In.ReadLine())){[Console]::Out.WriteLine($l);[Console]::Out.Flush()}\"",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        return new ProcessStartInfo
        {
            FileName = "cat",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    private static string EchoCommand => OperatingSystem.IsWindows() ? "powershell.exe" : "cat";

    [Fact]
    public async Task ShouldWriteToStdinAndReadStdout_WhenSendingRequest()
    {
        // We use a simple echo process as a server:
        // whatever we write to stdin comes back on stdout.
        using var process = new SysProcess { StartInfo = CreateEchoProcessStartInfo() };
        process.Start();

        await using var transport = new StdioMcpTransportSut(process);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var request = new JsonRpcRequest
        {
            Method = "test",
            Id = JsonSerializer.SerializeToElement(1),
            Params = JsonElement.Parse("{\"hello\":\"world\"}")
        };

        // The echo process echoes the serialized request line back as is,
        // which is valid JSON that deserializes as a JsonRpcResponse
        // (with jsonrpc, method, id, params fields — result/error will be null).
        var response = await transport.SendRequestAsync(request, TestContext.Current.CancellationToken);

        Assert.NotNull(response);
        Assert.Equal(1, response.Id!.Value.GetInt32());
    }

    [Fact]
    public async Task ShouldReturnTrue_WhenConnected()
    {
        using var process = new SysProcess { StartInfo = CreateEchoProcessStartInfo() };
        process.Start();

        await using var transport = new StdioMcpTransportSut(process);
        Assert.False(transport.IsConnected);

        await transport.ConnectAsync(TestContext.Current.CancellationToken);
        Assert.True(transport.IsConnected);
    }

    [Fact]
    public async Task ShouldTerminateProcess_WhenDisposed()
    {
        using var process = new SysProcess { StartInfo = CreateEchoProcessStartInfo() };
        process.Start();

        var pid = process.Id;

        var transport = new StdioMcpTransportSut(process);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);
        Assert.True(transport.IsConnected);

        await transport.DisposeAsync();

        // After dispose, the transport should no longer be connected
        Assert.False(transport.IsConnected);

        // Verify the process with that PID is no longer running
        try
        {
            var check = SysProcess.GetProcessById(pid);
            // If we get here, the process exists but should have exited
            Assert.True(check.HasExited);
        }
        catch (ArgumentException)
        {
            // Process no longer exists — this is expected
        }
    }

    /// <summary>
    /// Starts a server that writes the value of <paramref name="variable"/> into a file, then echoes;
    /// returns what the child saw once it has answered a request.
    /// </summary>
    private static async Task<string> ObserveChildVariableAsync(string variable, Dictionary<string, string>? env)
    {
        var dir = Path.Combine(Path.GetTempPath(), "orkeon-f8-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "seen.txt");
            var config = OperatingSystem.IsWindows()
                ? new McpServerConfig
                {
                    Command = "powershell.exe",
                    Args = ["-NoProfile", "-NonInteractive", "-Command",
                        $"[IO.File]::WriteAllText('{file}', [Environment]::GetEnvironmentVariable('{variable}')); "
                        + "while($null -ne ($l=[Console]::In.ReadLine())){[Console]::Out.WriteLine($l);[Console]::Out.Flush()}"]
                }
                : new McpServerConfig
                {
                    Command = "sh",
                    Args = ["-c", $"printf %s \"${variable}\" > \"$1\"; exec cat", "sh", file]
                };
            if (env != null)
            {
                foreach (var kvp in env)
                    config.Env[kvp.Key] = kvp.Value;
            }

            await using var transport = new StdioMcpTransportSut(config);
            await transport.ConnectAsync(TestContext.Current.CancellationToken);
            using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            guard.CancelAfter(TimeSpan.FromSeconds(30));

            // The echo answers only after the variable has been written.
            await transport.SendRequestAsync(StdioMcpTransportTestsFixture.CreateRequest(id: 1), guard.Token);
            return await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    [Fact]
    [Trait("BR", "CH-15")]
    public async Task ShouldInheritTheParentVariable_WhenNoEnvEntryIsWritten()
    {
        var seen = await ObserveChildVariableAsync("PATH", env: null);

        Assert.Equal(Environment.GetEnvironmentVariable("PATH"), seen);
    }

    [Fact]
    [Trait("BR", "CH-15")]
    public async Task ShouldPassTheTextAsWritten_WhenAnEnvEntryIsWritten()
    {
        var seen = await ObserveChildVariableAsync(
            "ORKEON_F8_PROBE", new Dictionary<string, string> { ["ORKEON_F8_PROBE"] = "${SOMETHING}" });

        Assert.Equal("${SOMETHING}", seen);
    }

    [Fact]
    public async Task ShouldThrowInvalidOperation_WhenSendingRequestWhileNotConnected()
    {
        var config = new McpServerConfig { Command = EchoCommand };
        var transport = new StdioMcpTransportSut(config);

        var request = new JsonRpcRequest { Method = "test", Id = JsonSerializer.SerializeToElement(1) };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => transport.SendRequestAsync(request, TestContext.Current.CancellationToken));
        Assert.Contains("not connected", ex.Message);

        await transport.DisposeAsync();
    }
}
