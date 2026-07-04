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
            Id = 1,
            Params = JsonDocument.Parse("{\"hello\":\"world\"}").RootElement
        };

        // The echo process echoes the serialized request line back as is,
        // which is valid JSON that deserializes as a JsonRpcResponse
        // (with jsonrpc, method, id, params fields — result/error will be null).
        var response = await transport.SendRequestAsync(request, TestContext.Current.CancellationToken);

        Assert.NotNull(response);
        Assert.Equal(1, response.Id);
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

    [Fact]
    public async Task ShouldThrowInvalidOperation_WhenSendingRequestWhileNotConnected()
    {
        var config = new McpServerConfig { Command = EchoCommand };
        var transport = new StdioMcpTransportSut(config);

        var request = new JsonRpcRequest { Method = "test", Id = 1 };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => transport.SendRequestAsync(request, TestContext.Current.CancellationToken));
        Assert.Contains("not connected", ex.Message);

        await transport.DisposeAsync();
    }
}
