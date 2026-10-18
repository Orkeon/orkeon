using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Execution;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.Timing;
using Orkeon.Tools.Code.Confinement;
using Orkeon.Tools.Code.Sessions;
using Orkeon.Tools.Code.Sessions.Windows;
using Orkeon.Tools.Code.Tests.Doubles;

namespace Orkeon.Tools.Code.Tests.Sessions.Windows;

/// <summary>
/// The AppContainer session for real, on Windows: a read-only mount read but not written, a read-write
/// mount written, nothing outside the mounts, no network, one session reused, nothing left once it ends,
/// and the leftovers of a crashed host swept. Integration: skipped off Windows and where the probe
/// cannot start an AppContainer; with <c>ORKEON_REQUIRE_APPCONTAINER=1</c> (the CI job) a failed probe
/// fails instead.
/// </summary>
[Trait("Category", "Integration")]
[SupportedOSPlatform("windows")]
public sealed class AppContainerShellSessionTests : IDisposable
{
    private readonly string _root;
    private readonly string _ledgers;
    private readonly AppContainerShellSandbox _sandbox;

    public AppContainerShellSessionTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindowsVersionAtLeast(10), "AppContainer runs on Windows 10 and later only.");
        var availability = AppContainerShellSandbox.Shared.Availability;
        if (Environment.GetEnvironmentVariable("ORKEON_REQUIRE_APPCONTAINER") == "1")
            Assert.True(availability.IsAvailable, $"AppContainer is required here but cannot run: {availability.Reason}");
        Assert.SkipUnless(availability.IsAvailable, $"AppContainer cannot run here: {availability.Reason}.");

        _root = Directory.CreateTempSubdirectory("orkeon-appcontainer-").FullName;
        _ledgers = Directory.CreateDirectory(Path.Combine(_root, "ledgers")).FullName;
        _sandbox = new AppContainerShellSandbox(() => ShellSandboxAvailability.Available, _ledgers);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ } catch (UnauthorizedAccessException) { /* best-effort */ }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ShellInvocation Builtin(string builtin, string? directory, params string[] arguments)
        => new(builtin, arguments, directory, TimeSpan.FromSeconds(30), WindowsBuiltin: true);

    private string Folder(string name, params (string File, string Content)[] files)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        foreach (var (file, content) in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(folder, file))!);
            File.WriteAllText(Path.Combine(folder, file), content);
        }

        return folder;
    }

    [Fact]
    public async Task ShouldReadAReadOnlyMount_ButNotWriteIt()
    {
        var docs = Folder("docs", ("a.txt", "confined"));
        await using var session = await _sandbox.OpenAsync(new ShellSessionView([new ConfinedPath(docs, "/docs", ConfinedAccess.ReadOnly)], [], false), Ct);

        var read = await session.RunAsync(Builtin("type", docs, "a.txt"), Ct);
        var write = await session.RunAsync(Builtin("mkdir", docs, "made"), Ct);

        Assert.Equal(0, read.ExitCode);
        Assert.Contains("confined", read.Stdout, StringComparison.Ordinal);
        Assert.NotEqual(0, write.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(docs, "made")));
    }

    [Fact]
    public async Task ShouldWriteAReadWriteMount_AndKeepTheSessionFromOneCommandToTheNext()
    {
        var workspace = Folder("ws");
        await using var session = await _sandbox.OpenAsync(new ShellSessionView([new ConfinedPath(workspace, "/workspace", ConfinedAccess.ReadWrite)], [], false), Ct);

        var made = await session.RunAsync(Builtin("mkdir", workspace, "made"), Ct);
        var listed = await session.RunAsync(Builtin("dir", workspace, "/b"), Ct);

        Assert.Equal(0, made.ExitCode);
        Assert.True(Directory.Exists(Path.Combine(workspace, "made")));
        Assert.Equal(0, listed.ExitCode);
        Assert.Contains("made", listed.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldRefuseAHiddenSubPath_AndAReadOnlySubPathOfAWritableMount()
    {
        var workspace = Folder("ws", ("secrets/key.txt", "s3cr3t"), ("vendor/lib.txt", "lib"));
        var view = new ShellSessionView(
        [
            new ConfinedPath(workspace, "/workspace", ConfinedAccess.ReadWrite),
            new ConfinedPath(Path.Combine(workspace, "secrets"), "/workspace/secrets", ConfinedAccess.HiddenDirectory),
            new ConfinedPath(Path.Combine(workspace, "vendor"), "/workspace/vendor", ConfinedAccess.ReadOnly),
        ], [], false);
        await using var session = await _sandbox.OpenAsync(view, Ct);

        var secret = await session.RunAsync(Builtin("type", workspace, @"secrets\key.txt"), Ct);
        var vendorRead = await session.RunAsync(Builtin("type", workspace, @"vendor\lib.txt"), Ct);
        var vendorWrite = await session.RunAsync(Builtin("mkdir", workspace, @"vendor\made"), Ct);

        Assert.NotEqual(0, secret.ExitCode);
        Assert.DoesNotContain("s3cr3t", secret.Stdout, StringComparison.Ordinal);
        Assert.Contains("lib", vendorRead.Stdout, StringComparison.Ordinal);
        Assert.NotEqual(0, vendorWrite.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(workspace, "vendor", "made")));
    }

    [Fact]
    public async Task ShouldRefuseReadingOutsideTheMounts()
    {
        var outside = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), $"orkeon-outside-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(outside, "host secret", Ct);
        try
        {
            var docs = Folder("docs", ("a.txt", "confined"));
            await using var session = await _sandbox.OpenAsync(new ShellSessionView([new ConfinedPath(docs, "/docs", ConfinedAccess.ReadOnly)], [], false), Ct);

            var result = await session.RunAsync(Builtin("type", docs, outside), Ct);

            Assert.NotEqual(0, result.ExitCode);
            Assert.DoesNotContain("host secret", result.Stdout, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task ShouldHaveNoNetwork_WhenTheViewDoesNotAllowIt()
    {
        var curl = Path.Combine(Environment.SystemDirectory, "curl.exe");
        Assert.SkipUnless(File.Exists(curl), "curl.exe is not in System32.");
        var probe = new ShellInvocation("curl.exe", ["-s", "-o", "NUL", "-m", "10", "https://www.microsoft.com/"], null, TimeSpan.FromSeconds(30));

        await using (var host = WindowsShellSession.OpenUnconfined())
            Assert.SkipUnless((await host.RunAsync(probe, Ct)).ExitCode == 0, "the host itself has no network.");

        await using var session = await _sandbox.OpenAsync(ShellSessionView.Empty, Ct);
        var confined = await session.RunAsync(probe, Ct);

        Assert.NotEqual(0, confined.ExitCode);
    }

    [Fact]
    public async Task ShouldKillEverythingTheSessionStarted_WhenTheRunEnds()
    {
        var scope = AgentRunScope.Begin("agent-appcontainer");
        var manager = new ShellSessionManager();
        var lingering = new ShellInvocation("cmd.exe", ["/d", "/c", "start", "/b", "ping", "-n", "120", "127.0.0.1"], null, TimeSpan.FromSeconds(30));
        var sandbox = new MockShellSandbox(_sandbox);

        await manager.RunAsync(sandbox, ShellSessionView.Empty, lingering, Ct);
        var pids = ((WindowsShellSession)Assert.Single(sandbox.Opened)).Job.ProcessIds();
        Assert.SkipWhen(pids.Count == 0, "ping does not linger in an AppContainer on this host.");

        await scope.DisposeAsync();

        await Polling.WaitUntilAsync(() => pids.All(HasExited), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ShouldKillEverythingAnUnconfinedSessionStarted_WhenItEnds()
    {
        var session = WindowsShellSession.OpenUnconfined();
        await session.RunAsync(new ShellInvocation("cmd.exe", ["/d", "/c", "start", "/b", "ping", "-n", "120", "127.0.0.1"], null, TimeSpan.FromSeconds(30)), Ct);
        var pids = session.Job.ProcessIds();
        Assert.NotEmpty(pids);

        await session.DisposeAsync();

        await Polling.WaitUntilAsync(() => pids.All(HasExited), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ShouldTakeItsAcesAndItsLedgerBack_WhenItEnds()
    {
        var docs = Folder("docs", ("a.txt", "confined"));
        var session = await _sandbox.OpenAsync(new ShellSessionView([new ConfinedPath(docs, "/docs", ConfinedAccess.ReadOnly)], [], false), Ct);
        var (_, ledger) = Assert.Single(new ShellSessionLedgerStore(_ledgers).ReadAll());
        Assert.NotNull(ledger);
        var sid = new SecurityIdentifier(ledger.ContainerSid);
        Assert.True(AclGrants.HasExplicitAce(docs, sid));

        await session.DisposeAsync();

        Assert.False(AclGrants.HasExplicitAce(docs, sid));
        Assert.Empty(new ShellSessionLedgerStore(_ledgers).ReadAll());
        Assert.False(ProfileExists(ledger.ProfileName));
    }

    [Fact]
    public async Task ShouldTimeOutAndKillTheJob()
    {
        // The timeout is the session's, confined or not; ping lingers reliably outside an AppContainer.
        await using var session = WindowsShellSession.OpenUnconfined();

        var result = await session.RunAsync(new ShellInvocation("ping", ["-n", "120", "127.0.0.1"], null, TimeSpan.FromSeconds(2)), Ct);

        Assert.True(result.TimedOut);
        await Polling.WaitUntilAsync(() => session.Job.ActiveProcessCount == 0, TimeSpan.FromSeconds(30));
        await Assert.ThrowsAsync<ShellSessionException>(() => session.RunAsync(new ShellInvocation("ping", ["-n", "1", "127.0.0.1"], null, TimeSpan.FromSeconds(30)), Ct));
    }

    [Fact]
    public async Task ShouldSweepTheLeftoversOfADeadHost_AndLeaveALiveOnesAlone()
    {
        var docs = Folder("docs", ("a.txt", "x"));
        var store = new ShellSessionLedgerStore(Path.Combine(_root, "sweep"));
        var dead = AppContainerProfile.Create(AppContainerProfile.NewName());
        var live = AppContainerProfile.Create(AppContainerProfile.NewName());
        try
        {
            AclGrants.Apply([new AclEntry(docs, AclRights.ReadAndExecute, Deny: false)], dead.Sid);
            AclGrants.Apply([new AclEntry(docs, AclRights.ReadAndExecute, Deny: false)], live.Sid);
            var (deadPid, deadStart) = await ExitedProcessAsync();
            store.Write(new ShellSessionLedger { ProfileName = dead.Name, ContainerSid = dead.Sid.Value, HostPid = deadPid, HostStartTicks = deadStart, GrantedPaths = [docs] });
            store.Write(new ShellSessionLedger
            {
                ProfileName = live.Name,
                ContainerSid = live.Sid.Value,
                HostPid = Environment.ProcessId,
                HostStartTicks = AppContainerShellSandbox.CurrentProcessStartTicks(),
                GrantedPaths = [docs],
            });

            var swept = AppContainerShellSandbox.SweepStaleSessions(store.Directory);

            Assert.Equal(1, swept);
            Assert.False(AclGrants.HasExplicitAce(docs, dead.Sid));
            Assert.False(ProfileExists(dead.Name));
            Assert.True(AclGrants.HasExplicitAce(docs, live.Sid));
            Assert.Equal(live.Name, Assert.Single(store.ReadAll()).ProfileName);
        }
        finally
        {
            AclGrants.Purge([docs], dead.Sid);
            AclGrants.Purge([docs], live.Sid);
            AppContainerProfile.Delete(dead.Name);
            AppContainerProfile.Delete(live.Name);
        }
    }

    [Fact]
    public async Task ShouldConfineTheShellTool_EndToEnd()
    {
        var docs = Folder("docs", ("a.txt", "confined"));
        var workspace = Folder("ws", ("secrets/key.txt", "s3cr3t"));
        using var registry = new FileSystemRegistry(
        [
            new FileSystemMount(docs, "/docs", FileAccessRights.ReadOnly),
            new FileSystemMount(workspace, "/workspace", FileAccessRights.ReadWrite, [new SubPathOverride("secrets", FileAccessRights.None)]),
        ]);
        using var tool = new ShellCommandTool(new FileSystemService(registry, new StubPathValidator().AllowAll(), NullLogger<FileSystemService>.Instance))
        {
            Confinement = new ShellConfinementOptions { Mode = ShellConfinementMode.AppContainer },
            Sandbox = _sandbox,
        };

        var read = Unpack(await tool.CallAsync(Request("type a.txt", "/docs"), Ct));
        var hidden = Unpack(await tool.CallAsync(Request(@"type secrets\key.txt", "/workspace"), Ct));

        Assert.Contains("confined", read["stdout"]!.ToString(), StringComparison.Ordinal);
        Assert.NotEqual(0, (int)hidden["exit_code"]!);
        Assert.DoesNotContain("s3cr3t", hidden["stdout"]!.ToString(), StringComparison.Ordinal);
    }

    private static ToolCallRequest Request(string command, string workingDirectory) =>
        new(ToolName: "shell_command", Parameters: new Dictionary<string, object?>
        {
            ["command"] = command,
            ["working_directory"] = workingDirectory,
        });

    private static Dictionary<string, object?> Unpack(ToolCallResponse result)
    {
        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        return dict;
    }

    private static bool HasExited(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static async Task<(int Pid, long StartTicks)> ExitedProcessAsync()
    {
        using var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /c exit 0")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        var start = process.StartTime.ToUniversalTime().Ticks;
        await process.WaitForExitAsync(Ct);
        return (process.Id, start);
    }

    /// <summary>Whether an AppContainer profile of that name exists: creating it again says so.</summary>
    private static bool ProfileExists(string name)
    {
        var hr = NativeMethods.CreateAppContainerProfile(name, name, "probe", 0, 0, out var sid);
        if (hr >= 0)
        {
            NativeMethods.FreeSid(sid);
            AppContainerProfile.Delete(name);
        }

        return hr == NativeMethods.HResultAlreadyExists;
    }
}
