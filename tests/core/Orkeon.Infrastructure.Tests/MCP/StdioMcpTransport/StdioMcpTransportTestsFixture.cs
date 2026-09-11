using System.Diagnostics;
using System.Text.Json;
using Orkeon.Infrastructure.MCP;
using SysProcess = System.Diagnostics.Process;

namespace Orkeon.Infrastructure.Tests.MCP;

public class StdioMcpTransportTestsFixture
{
    // --- Process creation ---

    public static ProcessStartInfo CreateEchoProcessStartInfo()
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

    public static string EchoCommand => OperatingSystem.IsWindows() ? "powershell.exe" : "cat";

    public static SysProcess StartEchoProcess()
    {
        var process = new SysProcess { StartInfo = CreateEchoProcessStartInfo() };
        process.Start();
        return process;
    }

    // --- Transport creation ---

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The process is captured by the returned transport and must outlive this factory; it lives for the duration of the test.")]
    public static async Task<StdioMcpTransport> CreateConnectedTransport()
    {
        var process = StartEchoProcess();
        var transport = new StdioMcpTransport(process);
        await transport.ConnectAsync();
        return transport;
    }

    public static StdioMcpTransport CreateUnconnectedTransport()
    {
        var config = new McpServerConfig { Command = EchoCommand };
        return new StdioMcpTransport(config);
    }

    // --- Request factory ---

    public static JsonRpcRequest CreateRequest(string method = "test", int id = 1, string? paramsJson = null)
    {
        var request = new JsonRpcRequest
        {
            Method = method,
            Id = JsonSerializer.SerializeToElement(id)
        };
        if (paramsJson != null)
            request.Params = JsonElement.Parse(paramsJson);
        return request;
    }
}
