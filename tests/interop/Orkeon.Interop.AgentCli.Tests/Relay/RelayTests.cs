using System.Net.Sockets;
using System.Text;
using Orkeon.Tests.Shared.Timing;
using RelayProgram = Orkeon.Interop.AgentCli.Relay.Program;
using SysProcess = System.Diagnostics.Process;
using SysProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace Orkeon.Interop.AgentCli.Tests.Relay;

public sealed class RelayTests
{
    [Theory]
    [InlineData("{\"orkeon_relay\":{\"pid\":42}}", 42)]
    [InlineData("{\"jsonrpc\":\"2.0\"}", null)]
    [InlineData("garbage", null)]
    [InlineData("", null)]
    public void ShouldReadGuardedPid_WhenHostSendsPreamble(string line, int? expected)
    {
        Assert.Equal(expected, RelayProgram.ReadGuardedPid(line));
    }

    [Theory]
    [InlineData("/tmp/orkeon-agent-cli-Ab12Cd/b.sock", "/tmp/orkeon-agent-cli-Ab12Cd")]
    [InlineData("/tmp/b.sock", null)]
    [InlineData("/home/user/project/b.sock", null)]
    [InlineData("b.sock", null)]
    public void ShouldNameOnlyHostScratch_WhenRelayCleansUp(string socketPath, string? expected)
    {
        Assert.Equal(expected?.Replace('/', Path.DirectorySeparatorChar), RelayProgram.ScratchFolderOf(socketPath.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>
    /// Probe 27 again: the host goes away (its socket closes), and the relay kills the process it guards — the
    /// CLI in production, a <c>sleep</c> here — with its tree.
    /// </summary>
    [Fact]
    public async Task ShouldKillProcessGroup_WhenHostConnectionDrops()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the guarded stand-in is the POSIX sleep");
        using var guard = new CancellationTokenSource(HangGuards.ProcessStart);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(guard.Token, TestContext.Current.CancellationToken);
        var folder = Directory.CreateTempSubdirectory(RelayProgram.ScratchPrefix).FullName;
        try
        {
            var socketPath = Path.Join(folder, "b.sock");
            using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(socketPath));
            listener.Listen(1);

            using var guarded = SysProcess.Start(new SysProcessStartInfo("sleep", "300") { UseShellExecute = false })!;
            using var relay = SysProcess.Start(new SysProcessStartInfo(Path.Join(AppContext.BaseDirectory, "Orkeon.Interop.AgentCli.Relay"), socketPath)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
            })!;

            using (var connection = await listener.AcceptAsync(linked.Token))
            {
                await connection.SendAsync(Encoding.UTF8.GetBytes($"{{\"orkeon_relay\":{{\"pid\":{guarded.Id}}}}}\n"), linked.Token);
                // The host dies: its end of the socket closes without a word.
            }

            await guarded.WaitForExitAsync(linked.Token);
            await relay.WaitForExitAsync(linked.Token);

            Assert.True(guarded.HasExited);
            Assert.Equal(0, relay.ExitCode);
            // The host left nothing behind: the relay removed its scratch folder before the kill.
            Assert.False(Directory.Exists(folder));
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ShouldPumpLinesBothWays_WhenHostAndCliTalk()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the relay's apphost name has no .exe here");
        using var guard = new CancellationTokenSource(HangGuards.ProcessStart);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(guard.Token, TestContext.Current.CancellationToken);
        var folder = Directory.CreateTempSubdirectory("orkeon-relay-test-").FullName;
        try
        {
            var socketPath = Path.Join(folder, "b.sock");
            using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(socketPath));
            listener.Listen(1);

            using var relay = SysProcess.Start(new SysProcessStartInfo(Path.Join(AppContext.BaseDirectory, "Orkeon.Interop.AgentCli.Relay"), socketPath)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
            })!;
            using var connection = await listener.AcceptAsync(linked.Token);
            await using var stream = new NetworkStream(connection);
            using var reader = new StreamReader(stream);
            await stream.WriteAsync(Encoding.UTF8.GetBytes("{\"orkeon_relay\":{\"pid\":0}}\n{\"to\":\"cli\"}\n"), linked.Token);

            var toCli = await relay.StandardOutput.ReadLineAsync(linked.Token);
            await relay.StandardInput.WriteLineAsync("{\"to\":\"host\"}");
            await relay.StandardInput.FlushAsync(linked.Token);
            var toHost = await reader.ReadLineAsync(linked.Token);
            relay.StandardInput.Close();
            await relay.WaitForExitAsync(linked.Token);

            Assert.Equal("{\"to\":\"cli\"}", toCli);
            Assert.Equal("{\"to\":\"host\"}", toHost);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
