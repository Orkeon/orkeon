using Orkeon.Infrastructure.MCP;
using StdioMcpTransportSut = Orkeon.Infrastructure.MCP.StdioMcpTransport;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>
/// GAP-35 — the transport reads its server's stderr for as long as the process lives. Nothing read
/// it: a server writing more than the pipe holds (64 KiB on Linux) blocked on its next write, and
/// its answers with it; and the reason a server gave as it stopped — <c>orkeon mcp serve</c>
/// refusing to start under another one — never reached the process that started it.
/// </summary>
public sealed class StdioMcpTransportStandardErrorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A server running <paramref name="posix"/> under sh, or <paramref name="windows"/> under PowerShell.</summary>
    private static McpServerConfig Server(string posix, string windows) => OperatingSystem.IsWindows()
        ? new McpServerConfig { Command = "powershell.exe", Args = ["-NoProfile", "-NonInteractive", "-Command", windows] }
        : new McpServerConfig { Command = "sh", Args = ["-c", posix] };

    [Fact]
    public async Task A_server_that_writes_a_mebibyte_on_stderr_before_answering_still_answers()
    {
        var config = Server(
            posix: "yes 'a line of server diagnostics' | head -c 1048576 >&2; exec cat",
            windows: "[Console]::Error.Write(('a line of server diagnostics' + [Environment]::NewLine) * 36200); "
                + "while($null -ne ($l=[Console]::In.ReadLine())){[Console]::Out.WriteLine($l);[Console]::Out.Flush()}");
        await using var transport = new StdioMcpTransportSut(config);
        await transport.ConnectAsync(Ct);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));

        // The echo server sends the request back: any answer proves the server got past its stderr.
        var response = await transport.SendRequestAsync(StdioMcpTransportTestsFixture.CreateRequest(id: 7), deadline.Token);

        Assert.Equal(7, response.Id!.Value.GetInt32());
    }

    [Fact]
    public async Task A_server_that_stops_after_a_line_on_stderr_fails_its_request_citing_that_line()
    {
        var config = Server(
            posix: "echo 'orkeon mcp serve: refused to start: started by another' >&2; exit 3",
            windows: "[Console]::Error.WriteLine('orkeon mcp serve: refused to start: started by another'); exit 3");
        await using var transport = new StdioMcpTransportSut(config);
        await transport.ConnectAsync(Ct);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => transport.SendRequestAsync(StdioMcpTransportTestsFixture.CreateRequest(id: 1), deadline.Token));

        Assert.Contains("orkeon mcp serve: refused to start: started by another", error.Message, StringComparison.Ordinal);
        Assert.Contains("code 3", error.Message, StringComparison.Ordinal);
    }
}
