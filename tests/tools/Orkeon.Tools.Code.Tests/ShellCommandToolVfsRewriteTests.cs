using System.Runtime.InteropServices;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Tools.Code.Tests;

/// <summary>
/// VFS path rewriting in <see cref="ShellCommandTool"/>: mount-prefixed virtual
/// arguments are resolved to physical paths before the process starts (a denied path
/// fails the call), and physical mount bases are rewritten back to virtual prefixes
/// in stdout/stderr — the model must only ever see virtual paths.
/// </summary>
public sealed class ShellCommandToolVfsRewriteTests : IDisposable
{
    private static readonly bool s_isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static readonly string[] s_sleepEchoAllowlist = s_isWindows
        ? ["ping", "echo"]
        : ["sleep", "echo"];

    private readonly string _tempDir;

    public ShellCommandToolVfsRewriteTests()
    {
        _tempDir = Directory.CreateTempSubdirectory("orkeon-shell-vfs-").FullName;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private static ToolCallRequest Request(string command, int? timeoutSeconds = null, string? workingDirectory = null)
    {
        var parameters = new Dictionary<string, object?> { ["command"] = command };
        if (timeoutSeconds is not null)
            parameters["timeout_seconds"] = timeoutSeconds;
        if (workingDirectory is not null)
            parameters["working_directory"] = workingDirectory;
        return new ToolCallRequest(ToolName: "shell_command", Parameters: parameters);
    }

    private static Dictionary<string, object?> Unpack(ToolCallResponse result)
    {
        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        return dict;
    }

    // ── inbound: virtual → physical ─────────────────────────────────────────

    [Fact]
    public async Task ShouldRewriteVirtualArgument_WhenTokenMatchesMountPrefix()
    {
        // Root resolution disabled ⇒ empty outbound table ⇒ echo shows the raw
        // physical argument, proving the INBOUND rewrite alone.
        using var tool = new ShellCommandTool(
            new StubMappingFileSystemService().AddMount("/ws", "/phys/root").WithFailingRootResolution());

        var dict = Unpack(await tool.CallAsync(Request("echo /ws/dist"), TestContext.Current.CancellationToken));

        Assert.Contains("/phys/root/dist", dict["stdout"]!.ToString());
    }

    [Fact]
    public async Task ShouldRewriteEmbeddedVirtualArgument_WhenTokenContainsEquals()
    {
        using var tool = new ShellCommandTool(
            new StubMappingFileSystemService().AddMount("/ws", "/phys/root").WithFailingRootResolution());

        var dict = Unpack(await tool.CallAsync(Request("echo --out=/ws/dist"), TestContext.Current.CancellationToken));

        Assert.Contains("--out=/phys/root/dist", dict["stdout"]!.ToString());
    }

    [Fact]
    public async Task ShouldRewriteOnlyFirstEquals_WhenTokenHasMultipleEquals()
    {
        using var tool = new ShellCommandTool(
            new StubMappingFileSystemService().AddMount("/ws", "/phys/root").WithFailingRootResolution());

        var dict = Unpack(await tool.CallAsync(Request("echo --out=/ws/a=b"), TestContext.Current.CancellationToken));

        // The entire right side of the FIRST '=' is resolved as one path.
        Assert.Contains("--out=/phys/root/a=b", dict["stdout"]!.ToString());
    }

    [Fact]
    public async Task ShouldNotRewriteArgument_WhenPrefixBoundaryDoesNotMatch()
    {
        using var tool = new ShellCommandTool(
            new StubMappingFileSystemService().AddMount("/ws", "/phys/root").WithFailingRootResolution());

        var dict = Unpack(await tool.CallAsync(Request("echo /wsx/dist"), TestContext.Current.CancellationToken));

        Assert.Contains("/wsx/dist", dict["stdout"]!.ToString());
        Assert.DoesNotContain("/phys/root", dict["stdout"]!.ToString());
    }

    [Fact]
    public async Task ShouldFailToolCall_WhenVirtualArgumentIsDenied()
    {
        using var tool = new ShellCommandTool(
            new StubMappingFileSystemService().AddMount("/ws", "/phys/root").Deny("/ws/secret"));

        var result = await tool.CallAsync(Request("echo /ws/secret"), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("denied", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/phys/root", result.Error);
    }

    [Fact]
    public async Task ShouldFailToolCall_WhenEmbeddedVirtualArgumentIsDenied()
    {
        using var tool = new ShellCommandTool(
            new StubMappingFileSystemService().AddMount("/ws", "/phys/root").Deny("/ws/secret"));

        var result = await tool.CallAsync(Request("echo --out=/ws/secret"), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("denied", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldKeepArgumentsVerbatim_WhenNoMountsConfigured()
    {
        // Regression guard for every pre-existing test: the zero-mount fake makes
        // both rewrite passes exact no-ops.
        using var tool = new ShellCommandTool(new FakeFileSystemService());

        var dict = Unpack(await tool.CallAsync(Request("echo /workspace/x"), TestContext.Current.CancellationToken));

        Assert.Contains("/workspace/x", dict["stdout"]!.ToString());
    }

    [Fact]
    public async Task ShouldResolveVirtualFileArgument_WhenUsingDiskBackedService()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_tempDir, "hello.txt"), "hello", TestContext.Current.CancellationToken);
        using var tool = new ShellCommandTool(new DiskBackedFileSystemService(_tempDir, "/src"));

        var command = s_isWindows ? "type /src/hello.txt" : "cat /src/hello.txt";
        var dict = Unpack(await tool.CallAsync(
            Request(command, workingDirectory: "/src"), TestContext.Current.CancellationToken));

        // End-to-end proof: '/src/hello.txt' exists nowhere physically.
        Assert.Equal(0, (int)dict["exit_code"]!);
        Assert.Contains("hello", dict["stdout"]!.ToString());
    }

    [Fact]
    public async Task ShouldRewriteExactMountRootArgument_WhenTokenEqualsMountPrefix()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_tempDir, "hello.txt"), "hello", TestContext.Current.CancellationToken);
        using var tool = new ShellCommandTool(new DiskBackedFileSystemService(_tempDir, "/src"));

        var command = s_isWindows ? "dir /src" : "ls /src";
        var dict = Unpack(await tool.CallAsync(
            Request(command, workingDirectory: "/src"), TestContext.Current.CancellationToken));

        Assert.Equal(0, (int)dict["exit_code"]!);
        Assert.Contains("hello.txt", dict["stdout"]!.ToString());
    }

    // ── outbound: physical → virtual ────────────────────────────────────────

    [Fact]
    public async Task ShouldRewritePhysicalPathsToVirtual_WhenCommandOutputsPhysicalPaths()
    {
        var fs = new DiskBackedFileSystemService(_tempDir, "/src");
        using var tool = new ShellCommandTool(fs);

        // The physical base is passed as a NON mount-prefixed argument (verbatim
        // inbound), echoed back by the process, and must come home virtualized.
        var dict = Unpack(await tool.CallAsync(
            Request($"echo {_tempDir}{Path.DirectorySeparatorChar}hello.txt", workingDirectory: "/src"),
            TestContext.Current.CancellationToken));

        var stdout = dict["stdout"]!.ToString()!;
        Assert.Contains("/src/hello.txt", stdout);
        Assert.DoesNotContain(fs.PhysicalBase, stdout);
    }

    [Fact]
    public async Task ShouldRewritePhysicalPathsInStderr_WhenCommandFails()
    {
        var fs = new DiskBackedFileSystemService(_tempDir, "/src");
        using var tool = new ShellCommandTool(fs);

        var command = s_isWindows ? "dir /src/missing_dir_42" : "ls /src/missing_dir_42";
        var dict = Unpack(await tool.CallAsync(
            Request(command, workingDirectory: "/src"), TestContext.Current.CancellationToken));

        Assert.NotEqual(0, (int)dict["exit_code"]!);
        var stderr = dict["stderr"]!.ToString()!;
        var stdout = dict["stdout"]!.ToString()!;
        Assert.DoesNotContain(fs.PhysicalBase, stderr);
        Assert.DoesNotContain(fs.PhysicalBase, stdout);
        if (!s_isWindows)
            Assert.Contains("/src/missing_dir_42", stderr);
    }

    [Fact]
    public async Task ShouldSkipMount_WhenRootResolutionDenied()
    {
        using var tool = new ShellCommandTool(
            new StubMappingFileSystemService().AddMount("/ws", "/phys/root").WithFailingRootResolution());

        // '/phys/root/file' is not mount-prefixed (inbound verbatim); with the root
        // resolution failing, the outbound table is empty, so it stays physical.
        var dict = Unpack(await tool.CallAsync(Request("echo /phys/root/file"), TestContext.Current.CancellationToken));

        Assert.Contains("/phys/root/file", dict["stdout"]!.ToString());
    }

    [Fact]
    public async Task ShouldSkipMount_WhenPhysicalEqualsVirtual()
    {
        // An identity mount (virtual IS the resolved path, like the in-memory fakes)
        // must not enter the outbound table.
        using var tool = new ShellCommandTool(
            new StubMappingFileSystemService().AddMount("/data", "/data"));

        var dict = Unpack(await tool.CallAsync(Request("echo /data/x"), TestContext.Current.CancellationToken));

        Assert.Contains("/data/x", dict["stdout"]!.ToString());
    }

    [Fact]
    public async Task ShouldReturnTimeoutMarker_WhenMountsConfiguredAndCommandTimesOut()
    {
        var command = s_isWindows ? "ping -n 11 127.0.0.1" : "sleep 10";
        using var tool = new ShellCommandTool(
            new DiskBackedFileSystemService(_tempDir, "/src"),
            allowedCommands: s_sleepEchoAllowlist);

        var dict = Unpack(await tool.CallAsync(
            Request(command, timeoutSeconds: 1, workingDirectory: "/src"),
            TestContext.Current.CancellationToken));

        Assert.False((bool)dict["completed"]!);
        Assert.Contains("[Command timed out]", dict["stderr"]!.ToString());
    }

    // ── pure helpers ────────────────────────────────────────────────────────

    [Fact]
    public void RewriteOutboundPaths_ShouldNormalizeBackslashes_AfterBaseReplacement()
    {
        var rewritten = ShellCommandTool.RewriteOutboundPaths(
            @"C:\ws\Foo.cs(12,3): error CS1002",
            [(@"C:\ws", "/workspace")]);

        Assert.Equal("/workspace/Foo.cs(12,3): error CS1002", rewritten);
    }

    [Fact]
    public void RewriteOutboundPaths_ShouldPreferLongestPhysicalBase_WhenBasesNest()
    {
        // The table is pre-sorted longest-first by BuildOutboundReplacements; the
        // scanner honours the order.
        var rewritten = ShellCommandTool.RewriteOutboundPaths(
            "built /phys/sub/a.dll and /phys/b.dll",
            [("/phys/sub", "/inner"), ("/phys", "/outer")]);

        Assert.Equal("built /inner/a.dll and /outer/b.dll", rewritten);
    }

    [Fact]
    public void RewriteOutboundPaths_ShouldRespectPathBoundary_WhenBaseIsPrefixOfAnotherPath()
    {
        var rewritten = ShellCommandTool.RewriteOutboundPaths(
            "see /data2/x and /data/y",
            [("/data", "/mnt")]);

        Assert.Equal("see /data2/x and /mnt/y", rewritten);
    }

    [Fact]
    public void RewriteOutboundPaths_ShouldStopNormalizationAtDelimiters()
    {
        var rewritten = ShellCommandTool.RewriteOutboundPaths(
            "\"C:\\ws\\a\\b.txt\" (C:\\ws\\c.txt) C:\\other\\d.txt",
            [(@"C:\ws", "/workspace")]);

        Assert.Equal("\"/workspace/a/b.txt\" (/workspace/c.txt) C:\\other\\d.txt", rewritten);
    }

    [Fact]
    public void RewriteOutboundPaths_ShouldRewriteEveryEntry_WhenOutputListsPathsWithSeparators()
    {
        // PATH-style lists (':' on Unix, ';' on Windows, ',' in tool output) must
        // delimit the token scan — otherwise the second base is swallowed raw and
        // leaks unrewritten (and, on Windows, gets its backslashes mangled).
        Assert.Equal(
            "/src/a.dll:/src/b.dll",
            ShellCommandTool.RewriteOutboundPaths("/phys/a.dll:/phys/b.dll", [("/phys", "/src")]));
        Assert.Equal(
            "/workspace/a;/workspace/b",
            ShellCommandTool.RewriteOutboundPaths(@"C:\ws\a;C:\ws\b", [(@"C:\ws", "/workspace")]));
        Assert.Equal(
            "probing paths: /src/x.cs,/src/y.cs",
            ShellCommandTool.RewriteOutboundPaths("probing paths: /phys/x.cs,/phys/y.cs", [("/phys", "/src")]));
    }

    [Fact]
    public void MatchesMountPrefix_ShouldRejectSiblingPrefix()
    {
        Assert.True(ShellCommandTool.MatchesMountPrefix("/workspace", "/workspace"));
        Assert.True(ShellCommandTool.MatchesMountPrefix("/workspace/x", "/workspace"));
        Assert.True(ShellCommandTool.MatchesMountPrefix("/workspace/x", "/workspace/"));
        Assert.False(ShellCommandTool.MatchesMountPrefix("/workspaces/x", "/workspace"));
        Assert.False(ShellCommandTool.MatchesMountPrefix("/work", "/workspace"));
    }

    // ── hand-written double ─────────────────────────────────────────────────

    /// <summary>
    /// Mapping stub: virtual mount prefix → physical base (plain string mapping, '/'
    /// separators), with a deny-set and an optional "mount root resolution fails"
    /// switch to keep the outbound table empty while inbound rewriting stays active.
    /// Only the members ShellCommandTool touches are implemented.
    /// </summary>
    private sealed class StubMappingFileSystemService : IFileSystemService
    {
        private readonly List<(string VirtualPrefix, string PhysicalBase)> _mounts = [];
        private readonly HashSet<string> _denied = new(StringComparer.Ordinal);
        private bool _failRootResolution;

        public StubMappingFileSystemService AddMount(string virtualPrefix, string physicalBase)
        {
            _mounts.Add((virtualPrefix, physicalBase));
            return this;
        }

        public StubMappingFileSystemService Deny(string virtualPath)
        {
            _denied.Add(virtualPath);
            return this;
        }

        public StubMappingFileSystemService WithFailingRootResolution()
        {
            _failRootResolution = true;
            return this;
        }

        public PathValidationResult ResolveAndValidate(string virtualPath, FileAccessRights requiredRight)
        {
            // Working-directory pass-through ('.'), like the zero-mount fake: these
            // tests exercise ARGUMENT rewriting, not working-directory validation.
            if (virtualPath == ".")
                return PathValidationResult.Allowed(".");

            if (_denied.Contains(virtualPath))
                return PathValidationResult.Denied($"Access denied for '{virtualPath}'.");

            foreach (var (prefix, physicalBase) in _mounts)
            {
                if (string.Equals(virtualPath, prefix, StringComparison.Ordinal))
                    return _failRootResolution
                        ? PathValidationResult.Denied($"Root resolution unavailable for '{prefix}'.")
                        : PathValidationResult.Allowed(physicalBase);

                if (virtualPath.StartsWith(prefix + "/", StringComparison.Ordinal))
                    return PathValidationResult.Allowed(physicalBase + virtualPath[prefix.Length..]);
            }

            return PathValidationResult.Denied($"No mount for virtual path '{virtualPath}'.");
        }

        public string? ToVirtualPath(string physicalPath)
        {
            foreach (var (prefix, physicalBase) in _mounts)
            {
                if (physicalPath.StartsWith(physicalBase, StringComparison.Ordinal))
                    return prefix + physicalPath[physicalBase.Length..];
            }

            return null;
        }

        public IReadOnlyList<MountInfo> GetAvailableMounts()
            => _mounts
                .Select(m => new MountInfo(m.VirtualPrefix, FileAccessRights.ReadWrite, []))
                .ToList();

        public IAsyncEnumerable<VirtualFileEntry> EnumerateFilesAsync(
            string virtualRoot, VirtualEnumerationOptions? options, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<Stream> OpenReadStreamAsync(string virtualPath, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<byte[]?> TryReadAllBytesAsync(string virtualPath, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<string?> TryReadAllTextAsync(string virtualPath, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<VirtualEntryKind> GetEntryKindAsync(string virtualPath, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<int> WriteAllTextAsync(string virtualPath, string content, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<bool> ExistsAsync(string virtualPath, CancellationToken ct)
            => throw new NotSupportedException();

        public Task CreateDirectoryAsync(string virtualPath, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string virtualPath, bool recursive, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<int> WriteAllBytesAsync(string virtualPath, byte[] content, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<int> AppendAllTextAsync(string virtualPath, string content, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<VirtualFileEntry?> TryGetEntryAsync(string virtualPath, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<Stream> OpenWriteStreamAsync(string virtualPath, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Stream> OpenAppendStreamAsync(string virtualPath, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task CopyAsync(string srcVirtualPath, string dstVirtualPath, bool overwrite = false, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
