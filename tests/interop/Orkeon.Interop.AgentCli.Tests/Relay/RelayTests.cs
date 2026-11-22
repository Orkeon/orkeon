using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Interop.AgentCli.Process;
using Orkeon.Tests.Shared.Timing;
using RelayProgram = Orkeon.Interop.AgentCli.Relay.Program;
using SysProcess = System.Diagnostics.Process;
using SysProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace Orkeon.Interop.AgentCli.Tests.Relay;

public sealed class RelayTests
{
    /// <summary>
    /// Starts the relay as the launcher has the CLI start it - its apphost in a host that carries its own
    /// runtime, else <c>dotnet exec</c> on its assembly - so the test holds where no apphost is produced.
    /// </summary>
    private static SysProcess StartRelay(string socketPath, string folder)
    {
        var (command, arguments) = new AgentCliProcessLauncher(NullLogger.Instance).RelayCommand(socketPath, folder);
        var startInfo = new SysProcessStartInfo(command)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        return SysProcess.Start(startInfo)!;
    }

    /// <summary>Ends a process the test started, with its tree, whatever the assertions did.</summary>
    private static void KillTree(SysProcess? process)
    {
        try
        {
            if (process is { HasExited: false })
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // It ended between the check and the kill.
        }
    }

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
        // A runtime configuration the launcher may write goes beside the socket's folder, never in it.
        var work = Directory.CreateTempSubdirectory("orkeon-relay-test-").FullName;
        SysProcess? guarded = null;
        SysProcess? relay = null;
        try
        {
            var socketPath = Path.Join(folder, "b.sock");
            using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(socketPath));
            listener.Listen(1);

            guarded = SysProcess.Start(new SysProcessStartInfo("sleep", "300") { UseShellExecute = false })!;
            relay = StartRelay(socketPath, work);

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
            KillTree(relay);
            KillTree(guarded);
            relay?.Dispose();
            guarded?.Dispose();
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
            Directory.Delete(work, recursive: true);
        }
    }

    [Fact]
    public async Task ShouldPumpLinesBothWays_WhenHostAndCliTalk()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the host's Unix domain socket is tested on POSIX hosts");
        using var guard = new CancellationTokenSource(HangGuards.ProcessStart);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(guard.Token, TestContext.Current.CancellationToken);
        var folder = Directory.CreateTempSubdirectory("orkeon-relay-test-").FullName;
        SysProcess? relay = null;
        try
        {
            var socketPath = Path.Join(folder, "b.sock");
            using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(socketPath));
            listener.Listen(1);

            relay = StartRelay(socketPath, folder);
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
            KillTree(relay);
            relay?.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }
}
