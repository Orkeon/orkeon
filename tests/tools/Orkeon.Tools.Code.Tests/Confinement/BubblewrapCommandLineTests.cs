using Orkeon.Tools.Code.Confinement;

namespace Orkeon.Tools.Code.Tests.Confinement;

/// <summary>
/// The arguments of <c>bwrap</c> that build a confined process's view from the VFS: namespaces, the
/// system directories, each mount at its virtual path with its rights, parents before what they hold.
/// </summary>
public sealed class BubblewrapCommandLineTests
{
    private static IReadOnlyList<string> Build(
        IEnumerable<ConfinedPath>? paths = null,
        IEnumerable<string>? readOnlyPaths = null,
        bool allowNetwork = false,
        string workingDirectory = "/workspace",
        IEnumerable<string>? socketPaths = null)
        => BubblewrapCommandLine.Build(paths ?? [], readOnlyPaths ?? [], allowNetwork, workingDirectory, socketPaths ?? []);

    private static int IndexOf(IReadOnlyList<string> arguments, params string[] sequence)
    {
        for (var i = 0; i + sequence.Length <= arguments.Count; i++)
        {
            if (sequence.Select((value, offset) => arguments[i + offset] == value).All(match => match))
                return i;
        }

        return -1;
    }

    [Fact]
    public void ShouldUnshareEveryNamespaceAndKeepTheNetworkOff_WhenTheNetworkIsNotAllowed()
    {
        var arguments = Build();

        Assert.Equal("--unshare-all", arguments[0]);
        Assert.DoesNotContain("--share-net", arguments);
        Assert.True(IndexOf(arguments, "--die-with-parent") >= 0);
        Assert.True(IndexOf(arguments, "--new-session") >= 0);
    }

    [Fact]
    public void ShouldShareTheNetwork_WhenAllowed()
    {
        Assert.Contains("--share-net", Build(allowNetwork: true));
    }

    [Fact]
    public void ShouldGiveAProcAFreshDevAndAnEmptyTmp()
    {
        var arguments = Build();

        Assert.True(IndexOf(arguments, "--proc", "/proc") >= 0);
        Assert.True(IndexOf(arguments, "--dev", "/dev") >= 0);
        Assert.True(IndexOf(arguments, "--tmpfs", "/tmp") >= 0);
    }

    [Fact]
    public void ShouldBindTheSystemDirectoriesAndTheExtraPathsReadOnlyAtTheirOwnPath()
    {
        var arguments = Build(readOnlyPaths: ["/opt/node", "/usr"]);

        foreach (var path in BubblewrapCommandLine.SystemPaths)
            Assert.True(IndexOf(arguments, "--ro-bind-try", path, path) >= 0, path);
        Assert.True(IndexOf(arguments, "--ro-bind-try", "/opt/node", "/opt/node") >= 0);
        // An extra path that is already a system directory is bound once.
        Assert.Equal(2, arguments.Count(argument => argument == "/usr"));
    }

    [Fact]
    public void ShouldBindEachMountAtItsVirtualPathWithItsRights()
    {
        var arguments = Build(
        [
            new ConfinedPath("/phys/docs", "/docs", ConfinedAccess.ReadOnly),
            new ConfinedPath("/phys/ws", "/workspace", ConfinedAccess.ReadWrite),
        ]);

        Assert.True(IndexOf(arguments, "--ro-bind-try", "/phys/docs", "/docs") >= 0);
        Assert.True(IndexOf(arguments, "--bind-try", "/phys/ws", "/workspace") >= 0);
    }

    [Fact]
    public void ShouldCoverAPathWithoutRights_WithAReadOnlyTmpfsOrDevNull()
    {
        var arguments = Build(
        [
            new ConfinedPath("/phys/ws/secrets", "/workspace/secrets", ConfinedAccess.HiddenDirectory),
            new ConfinedPath("/phys/ws/.env", "/workspace/.env", ConfinedAccess.HiddenFile),
        ]);

        Assert.True(IndexOf(arguments, "--tmpfs", "/workspace/secrets", "--remount-ro", "/workspace/secrets") >= 0);
        Assert.True(IndexOf(arguments, "--ro-bind", "/dev/null", "/workspace/.env") >= 0);
        Assert.DoesNotContain("/phys/ws/secrets", arguments);
    }

    [Fact]
    public void ShouldBindAParentBeforeWhatItHolds_WhateverTheOrderGiven()
    {
        var arguments = Build(
        [
            new ConfinedPath("/phys/ws/vendor", "/workspace/vendor", ConfinedAccess.ReadOnly),
            new ConfinedPath("/phys/ws", "/workspace", ConfinedAccess.ReadWrite),
        ]);

        var parent = IndexOf(arguments, "--bind-try", "/phys/ws", "/workspace");
        var child = IndexOf(arguments, "--ro-bind-try", "/phys/ws/vendor", "/workspace/vendor");
        Assert.True(parent >= 0 && child > parent);
    }

    [Fact]
    public void ShouldBindTheMountsAfterTheSystemDirectoriesAndTmp()
    {
        var arguments = Build([new ConfinedPath("/phys/tmp", "/tmp", ConfinedAccess.ReadWrite)]);

        var tmpfs = IndexOf(arguments, "--tmpfs", "/tmp");
        var mount = IndexOf(arguments, "--bind-try", "/phys/tmp", "/tmp");
        var lastSystem = IndexOf(arguments, "--ro-bind-try", "/etc", "/etc");
        Assert.True(mount > tmpfs && mount > lastSystem);
    }

    [Fact]
    public void ShouldEndWithTheEnvironmentTheViewNeedsTheWorkingDirectoryAndTheSeparator()
    {
        var arguments = Build(workingDirectory: "/workspace/app");

        Assert.Equal(["--chdir", "/workspace/app", "--"], arguments.TakeLast(3));
        Assert.True(IndexOf(arguments, "--setenv", "HOME", "/tmp") >= 0);
        Assert.True(IndexOf(arguments, "--setenv", "TMPDIR", "/tmp") >= 0);
    }

    [Fact]
    public void ShouldBindEachSessionSocketReadWriteAtItsOwnPath_AfterTheMounts_WhenTheNetworkIsAllowed()
    {
        var arguments = Build(
            [new ConfinedPath("/phys/ws", "/workspace", ConfinedAccess.ReadWrite)],
            allowNetwork: true,
            socketPaths: ["/run/user/1000/bus", "/tmp/ssh-x/agent.1"]);

        var bus = IndexOf(arguments, "--bind-try", "/run/user/1000/bus", "/run/user/1000/bus");
        Assert.True(bus > IndexOf(arguments, "--bind-try", "/phys/ws", "/workspace"));
        Assert.True(IndexOf(arguments, "--bind-try", "/tmp/ssh-x/agent.1", "/tmp/ssh-x/agent.1") > IndexOf(arguments, "--tmpfs", "/tmp"));
        Assert.True(bus < IndexOf(arguments, "--"));
    }

    [Fact]
    public void ShouldBindNoSessionSocket_WhenTheNetworkIsNotAllowed()
    {
        var arguments = Build(allowNetwork: false, socketPaths: ["/run/user/1000/bus"]);

        Assert.DoesNotContain("/run/user/1000/bus", arguments);
    }
}
