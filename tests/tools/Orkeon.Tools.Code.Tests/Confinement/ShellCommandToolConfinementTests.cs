using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tools.Code.Confinement;

namespace Orkeon.Tools.Code.Tests.Confinement;

/// <summary>
/// <see cref="ShellCommandTool"/> under <see cref="ShellConfinementMode.Bubblewrap"/>: the call fails with
/// the reason when bubblewrap is unavailable, and otherwise runs in a view built from the VFS. The view's
/// own behaviour (a read-only mount refused by the kernel) needs a working <c>bwrap</c>: those tests are
/// Integration and skip with the probe's reason elsewhere.
/// </summary>
public sealed class ShellCommandToolConfinementTests : IDisposable
{
    private static readonly bool s_isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private readonly string _tempDir;
    private readonly string _docs;
    private readonly string _workspace;

    public ShellCommandToolConfinementTests()
    {
        _tempDir = Directory.CreateTempSubdirectory("orkeon-shell-confined-").FullName;
        _docs = Directory.CreateDirectory(Path.Combine(_tempDir, "docs")).FullName;
        _workspace = Directory.CreateDirectory(Path.Combine(_tempDir, "ws")).FullName;
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ToolCallRequest Request(string command, string workingDirectory = "/workspace") =>
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

    /// <summary>
    /// A real file system: <c>/docs</c> read-only, <c>/workspace</c> read-write with a <c>secrets</c>
    /// folder it grants no right on.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The registry is captured by the returned FileSystemService and lives for the duration of the test.")]
    private FileSystemService FileSystem()
    {
        Directory.CreateDirectory(Path.Combine(_workspace, "secrets"));
        var registry = new FileSystemRegistry(
        [
            new FileSystemMount(_docs, "/docs", FileAccessRights.ReadOnly),
            new FileSystemMount(_workspace, "/workspace", FileAccessRights.ReadWrite,
                [new SubPathOverride("secrets", FileAccessRights.None)]),
        ]);
        return new FileSystemService(registry, new StubPathValidator().AllowAll(), NullLogger<FileSystemService>.Instance);
    }

    private ShellCommandTool Confined(
        Func<BubblewrapAvailability>? bubblewrap = null,
        IEnumerable<string>? extraAllowedCommands = null,
        IEnumerable<string>? readOnlyPaths = null)
    {
        var confinement = new ShellConfinementOptions { Mode = ShellConfinementMode.Bubblewrap };
        foreach (var path in readOnlyPaths ?? [])
            confinement.ReadOnlyPaths.Add(path);

        return bubblewrap is null
            ? new ShellCommandTool(FileSystem(), extraAllowedCommands: extraAllowedCommands) { Confinement = confinement }
            : new ShellCommandTool(FileSystem(), extraAllowedCommands: extraAllowedCommands)
            {
                Confinement = confinement,
                Bubblewrap = bubblewrap,
            };
    }

    // ── without bubblewrap ──────────────────────────────────────────────────

    [Fact]
    public async Task ShouldFailTheCallWithTheReason_WhenConfinementIsUnavailable()
    {
        using var tool = Confined(() => BubblewrapAvailability.Unavailable("bwrap was not found (install the bubblewrap package)"));

        var result = await tool.CallAsync(Request("echo confined"), Ct);

        Assert.False(result.Success);
        Assert.Null(result.Result);
        Assert.Contains("Confinement unavailable", result.Error!, StringComparison.Ordinal);
        Assert.Contains("bwrap was not found", result.Error!, StringComparison.Ordinal);
        Assert.Contains("The command did not run.", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldRefuseTheCall_WhenAnExtraReadOnlyPathIsRelative()
    {
        using var tool = Confined(() => BubblewrapAvailability.Available("/usr/bin/bwrap"), readOnlyPaths: ["opt/node"]);

        var result = await tool.CallAsync(Request("echo confined"), Ct);

        Assert.False(result.Success);
        Assert.Contains("'opt/node', which is not an absolute path", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldNeverProbeBubblewrap_WhenConfinementIsOff()
    {
        using var tool = new ShellCommandTool(FileSystem())
        {
            Bubblewrap = () => throw new InvalidOperationException("probed without confinement"),
        };

        var dict = Unpack(await tool.CallAsync(Request("echo unconfined"), Ct));

        Assert.Equal(0, (int)dict["exit_code"]!);
    }

    /// <summary>
    /// The whole wiring, with a stand-in for <c>bwrap</c> that records the arguments it is given: each
    /// mount at its virtual path with its rights, the folder without rights covered, the virtual working
    /// directory, then the command with its virtual arguments untouched.
    /// </summary>
    [Fact]
    public async Task ShouldStartBubblewrapWithTheViewOfTheMountsAndTheVirtualCommand()
    {
        Assert.SkipWhen(s_isWindows, "The stand-in for bwrap is a POSIX shell script.");
        var recorded = Path.Combine(_tempDir, "bwrap-arguments.txt");
        var standIn = Path.Combine(_tempDir, "bwrap");
        await File.WriteAllTextAsync(standIn, $"#!/bin/sh\nprintf '%s\\n' \"$@\" > '{recorded}'\n", Ct);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(standIn, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var tool = Confined(() => BubblewrapAvailability.Available(standIn));

        var dict = Unpack(await tool.CallAsync(Request("cat /docs/a.txt", workingDirectory: "/workspace"), Ct));

        Assert.Equal(0, (int)dict["exit_code"]!);
        var arguments = (await File.ReadAllLinesAsync(recorded, Ct)).ToList();
        Assert.Equal(["--", "cat", "/docs/a.txt"], arguments.TakeLast(3));
        Assert.Contains($"--ro-bind-try|{_docs}|/docs", Joined(arguments), StringComparison.Ordinal);
        Assert.Contains($"--bind-try|{_workspace}|/workspace", Joined(arguments), StringComparison.Ordinal);
        Assert.Contains("--tmpfs|/workspace/secrets|--remount-ro|/workspace/secrets", Joined(arguments), StringComparison.Ordinal);
        Assert.Contains("--chdir|/workspace|--", Joined(arguments), StringComparison.Ordinal);
    }

    private static string Joined(IEnumerable<string> arguments) => string.Join('|', arguments);

    // ── with bubblewrap (Integration) ───────────────────────────────────────

    private static void SkipUnlessBubblewrapWorks()
    {
        var bubblewrap = BubblewrapProbe.Current();
        Assert.SkipUnless(bubblewrap.IsAvailable, $"bubblewrap cannot confine a process here: {bubblewrap.Reason}.");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ShouldRefuseAWriteToAReadOnlyMount_WhenAScriptReachesItByItself()
    {
        SkipUnlessBubblewrapWorks();
        await File.WriteAllTextAsync(Path.Combine(_workspace, "w.sh"), "echo x > /docs/f\n", Ct);
        using var tool = Confined(extraAllowedCommands: ["bash"]);

        var dict = Unpack(await tool.CallAsync(Request("bash /workspace/w.sh"), Ct));

        Assert.NotEqual(0, (int)dict["exit_code"]!);
        Assert.Contains("Read-only file system", dict["stderr"]!.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_docs, "f")));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ShouldWriteToAWritableMount_WhenAScriptReachesItByItself()
    {
        SkipUnlessBubblewrapWorks();
        await File.WriteAllTextAsync(Path.Combine(_workspace, "w.sh"), "echo x > /workspace/out.txt\n", Ct);
        using var tool = Confined(extraAllowedCommands: ["bash"]);

        var dict = Unpack(await tool.CallAsync(Request("bash /workspace/w.sh"), Ct));

        Assert.Equal(0, (int)dict["exit_code"]!);
        Assert.True(File.Exists(Path.Combine(_workspace, "out.txt")));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ShouldShowTheMountsAndTheSystemDirectoriesOnly_WhenTheRootIsListed()
    {
        SkipUnlessBubblewrapWorks();
        using var tool = Confined();

        var dict = Unpack(await tool.CallAsync(Request("ls /"), Ct));

        var entries = dict["stdout"]!.ToString()!.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string[] allowed = ["bin", "dev", "docs", "etc", "lib", "lib32", "lib64", "libx32", "proc", "sbin", "tmp", "usr", "workspace"];
        Assert.All(entries, entry => Assert.Contains(entry, allowed));
        Assert.Contains("docs", entries);
        Assert.Contains("workspace", entries);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ShouldStartInTheVirtualWorkingDirectory()
    {
        SkipUnlessBubblewrapWorks();
        using var tool = Confined();

        var dict = Unpack(await tool.CallAsync(Request("pwd", workingDirectory: "/workspace"), Ct));

        Assert.Equal("/workspace", dict["stdout"]!.ToString()!.Trim());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ShouldHideAFolderTheVfsGrantsNoRightOn()
    {
        SkipUnlessBubblewrapWorks();
        await File.WriteAllTextAsync(Path.Combine(_workspace, "secrets", "key.txt"), "secret", Ct);
        using var tool = Confined();

        // Named, /workspace/secrets is refused by the VFS before the process starts; a recursive
        // listing of its parent reaches it by itself.
        var dict = Unpack(await tool.CallAsync(Request("ls -R /workspace"), Ct));

        Assert.Contains("secrets", dict["stdout"]!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("key.txt", dict["stdout"]!.ToString(), StringComparison.Ordinal);
    }
}
