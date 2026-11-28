using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tools.Code.Sessions;
using Orkeon.Tools.Code.Tests.Doubles;

namespace Orkeon.Tools.Code.Tests;

/// <summary>
/// A cut <c>run</c> output (#492): the result says it was cut, gives the true lengths, keeps the head and the
/// tail around the marker, and the whole output is in the file <c>output_path</c> names.
/// </summary>
public sealed class ShellCommandToolOutputTests : IDisposable
{
    private static readonly bool s_isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private readonly string _tempDir = Directory.CreateTempSubdirectory("orkeon-shell-output-").FullName;
    private readonly List<IDisposable> _disposables = [];
    private FileSystemService? _fileSystem;

    public void Dispose()
    {
        foreach (var disposable in _disposables)
            disposable.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Workspace => Path.Combine(_tempDir, "ws");

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The registry is captured by the returned FileSystemService and lives for the test.")]
    private ShellCommandTool NewTool(FakeShellSandbox? sandbox = null, ShellConfinementOptions? confinement = null, int? captureByteCap = null, int? maxCaptureFiles = null, bool withOutput = true)
    {
        var output = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(Workspace);
        Directory.CreateDirectory(output);
        var mounts = new List<FileSystemMount> { new(Workspace, "/workspace", FileAccessRights.ReadWrite) };
        if (withOutput)
            mounts.Add(new(output, "/output", FileAccessRights.ReadWrite));
        _fileSystem = new FileSystemService(
            new FileSystemRegistry(mounts), new StubPathValidator().AllowAll(), NullLogger<FileSystemService>.Instance);
        var tool = sandbox is null
            ? new ShellCommandTool(_fileSystem) { Confinement = confinement ?? TestConfinement.Off() }
            : new ShellCommandTool(_fileSystem) { Sandbox = sandbox, Confinement = TestConfinement.Off() };
        if (captureByteCap is { } cap)
            tool.CaptureByteCap = cap;
        if (maxCaptureFiles is { } files)
            tool.MaxCaptureFiles = files;
        _disposables.Add(tool);
        return tool;
    }

    // Numbered lines up to about `length` characters, the last one recognisable.
    private static string Lines(int length)
    {
        var builder = new StringBuilder();
        var line = 0;
        while (builder.Length < length - 40)
            builder.Append(CultureInfo.InvariantCulture, $"line {line++:D6} of the output\n");
        return builder.Append("LAST LINE OF THE OUTPUT\n").ToString();
    }

    private async Task<Dictionary<string, object?>> CatAsync(ShellCommandTool tool, string content)
    {
        await File.WriteAllTextAsync(Path.Combine(Workspace, "big.txt"), content, Ct);
        var command = s_isWindows ? "type /workspace/big.txt" : "cat /workspace/big.txt";
        var result = await tool.CallAsync(
            new ToolCallRequest("shell_command", new Dictionary<string, object?> { ["command"] = command }), Ct);
        Assert.True(result.Success, result.Error);
        return Assert.IsType<Dictionary<string, object?>>(result.Result);
    }

    [Theory]
    [Trait("BR", "CH-492")]
    [InlineData(30_000)]
    [InlineData(300_000)]
    public async Task ShouldSayTheOutputWasCut_AndKeepItWhole_WhenACommandPrintsMoreThanTheLimit(int length)
    {
        var tool = NewTool();
        var content = Lines(length);

        var dict = await CatAsync(tool, content);

        var expectedTotal = s_isWindows ? content.Replace("\n", "\r\n", StringComparison.Ordinal).Length : content.Length;
        Assert.True((bool)dict["stdout_truncated"]!);
        Assert.False((bool)dict["stderr_truncated"]!);
        Assert.Equal(expectedTotal, Convert.ToInt64(dict["stdout_total_chars"], CultureInfo.InvariantCulture));
        var stdout = Assert.IsType<string>(dict["stdout"]);
        Assert.StartsWith("line 000000 of the output", stdout, StringComparison.Ordinal);
        Assert.Contains("LAST LINE OF THE OUTPUT", stdout, StringComparison.Ordinal);
        Assert.Contains("chars omitted", stdout, StringComparison.Ordinal);
        Assert.True(stdout.Length < ShellCommandTool.MaxOutputLength + 200);

        var outputPath = Assert.IsType<string>(dict["output_path"]);
        Assert.StartsWith("/output/shell/", outputPath, StringComparison.Ordinal);
        var whole = await _fileSystem!.TryReadAllTextAsync(outputPath, Ct);
        Assert.Equal(content, whole!.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("BR", "CH-492")]
    public async Task ShouldSayTheOutputIsWhole_WhenACommandPrintsLessThanTheLimit()
    {
        var tool = NewTool();
        var content = new string('x', 99) + "\n";

        var dict = await CatAsync(tool, content);

        Assert.False((bool)dict["stdout_truncated"]!);
        Assert.False((bool)dict["stderr_truncated"]!);
        Assert.Equal(content.Length + (s_isWindows ? 1 : 0), Convert.ToInt64(dict["stdout_total_chars"], CultureInfo.InvariantCulture));
        Assert.Equal("", dict["output_path"]);
        Assert.False(Directory.Exists(Path.Combine(_tempDir, "out", "shell")) && Directory.EnumerateFiles(Path.Combine(_tempDir, "out", "shell")).Any());
    }

    [Fact]
    [Trait("BR", "CH-492")]
    public async Task ShouldWriteWhatTheSessionReturned_WhenTheSessionKeptNoCopy()
    {
        var head = new string('h', 20_000);
        var tail = new string('t', 20_000) + "\nTHE END";
        var sandbox = new FakeShellSandbox
        {
            Configure = session => session.DefaultResult = new ShellRunResult(
                0, head, "oops", StdoutTail: tail, StdoutOmitted: 1_000_000),
        };
        var tool = NewTool(sandbox);

        var result = await tool.CallAsync(
            new ToolCallRequest("shell_command", new Dictionary<string, object?> { ["command"] = "echo x" }), Ct);

        var dict = Assert.IsType<Dictionary<string, object?>>(result.Result);
        Assert.True((bool)dict["stdout_truncated"]!);
        Assert.Equal(head.Length + tail.Length + 1_000_000L, Convert.ToInt64(dict["stdout_total_chars"], CultureInfo.InvariantCulture));
        Assert.EndsWith("THE END", Assert.IsType<string>(dict["stdout"]), StringComparison.Ordinal);
        Assert.Equal("oops", dict["stderr"]);
        var whole = await _fileSystem!.TryReadAllTextAsync(Assert.IsType<string>(dict["output_path"]), Ct);
        Assert.StartsWith(head, whole, StringComparison.Ordinal);
        Assert.EndsWith(tail + ShellScript.CaptureStderrHeader + "oops", whole, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-492")]
    public async Task ShouldKeepTheHeadAndTheTail_WhenTheSessionDidNotCut()
    {
        var text = "BEGIN" + new string('m', 20_000) + "END";
        var sandbox = new FakeShellSandbox { Configure = session => session.DefaultResult = new ShellRunResult(0, text, "") };
        var tool = NewTool(sandbox);

        var result = await tool.CallAsync(
            new ToolCallRequest("shell_command", new Dictionary<string, object?> { ["command"] = "echo x" }), Ct);

        var dict = Assert.IsType<Dictionary<string, object?>>(result.Result);
        var stdout = Assert.IsType<string>(dict["stdout"]);
        Assert.StartsWith("BEGIN", stdout, StringComparison.Ordinal);
        Assert.EndsWith("END", stdout, StringComparison.Ordinal);
        Assert.Contains($"[... truncated, {text.Length - ShellCommandTool.MaxOutputLength} chars omitted", stdout, StringComparison.Ordinal);
        Assert.Equal((long)text.Length, Convert.ToInt64(dict["stdout_total_chars"], CultureInfo.InvariantCulture));
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("BR", "CH-492")]
    public async Task ShouldKeepTheWholeOutput_WhenTheSessionIsConfinedByBubblewrap()
    {
        var bubblewrap = Orkeon.Tools.Code.Confinement.BubblewrapProbe.Current();
        Assert.SkipUnless(bubblewrap.IsAvailable, $"bubblewrap cannot confine a process here: {bubblewrap.Reason}.");
        var tool = NewTool(confinement: new ShellConfinementOptions { Mode = ShellConfinementMode.Bubblewrap });
        var content = Lines(100_000);

        var dict = await CatAsync(tool, content);

        Assert.True((bool)dict["stdout_truncated"]!);
        Assert.Equal((long)content.Length, Convert.ToInt64(dict["stdout_total_chars"], CultureInfo.InvariantCulture));
        var whole = await _fileSystem!.TryReadAllTextAsync(Assert.IsType<string>(dict["output_path"]), Ct);
        Assert.Equal(content, whole);
    }

    private string ShellDirectory => Path.Combine(_tempDir, "out", "shell");

    private string[] ShellFiles() => Directory.Exists(ShellDirectory) ? Directory.GetFiles(ShellDirectory) : [];

    private static Task<ToolCallResponse> EchoAsync(ShellCommandTool tool) =>
        tool.CallAsync(new ToolCallRequest("shell_command", new Dictionary<string, object?> { ["command"] = "echo x" }), Ct);

    [Fact]
    [Trait("BR", "CH-492")]
    public async Task ShouldVirtualizeThePhysicalPaths_InTheCaptureFile()
    {
        Assert.SkipWhen(s_isWindows, "The POSIX session copies the output itself.");
        var tool = NewTool();
        var physical = Path.Combine(Workspace, "big.txt");
        var builder = new StringBuilder();
        for (var line = 0; builder.Length < 30_000; line++)
            builder.Append(CultureInfo.InvariantCulture, $"{physical}: line {line}\n");

        var dict = await CatAsync(tool, builder.ToString());

        var whole = await _fileSystem!.TryReadAllTextAsync(Assert.IsType<string>(dict["output_path"]), Ct);
        Assert.Contains("/workspace/big.txt: line 0\n", whole, StringComparison.Ordinal);
        Assert.DoesNotContain(_tempDir, whole, StringComparison.Ordinal);
        Assert.DoesNotContain(ShellFiles(), f => !f.EndsWith(".log", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("BR", "CH-492")]
    public async Task ShouldCountCharacters_NotBytes_WhenTheOutputIsNotAscii()
    {
        Assert.SkipWhen(s_isWindows, "The POSIX session counts its streams in bytes.");
        var tool = NewTool();
        var content = new string('\u00e9', 20_000) + "\n";

        var dict = await CatAsync(tool, content);

        Assert.True((bool)dict["stdout_truncated"]!);
        Assert.Equal((long)content.Length, Convert.ToInt64(dict["stdout_total_chars"], CultureInfo.InvariantCulture));
        var stdout = Assert.IsType<string>(dict["stdout"]);
        Assert.Contains($"[... truncated, {content.Length - ShellCommandTool.MaxOutputLength} chars omitted", stdout, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-492")]
    public async Task ShouldCapTheCaptureFile_AndSaySo()
    {
        Assert.SkipWhen(s_isWindows, "The POSIX session copies the output itself.");
        var tool = NewTool(captureByteCap: 50_000);
        var content = Lines(200_000);

        var dict = await CatAsync(tool, content);

        Assert.Equal((long)content.Length, Convert.ToInt64(dict["stdout_total_chars"], CultureInfo.InvariantCulture));
        var stdout = Assert.IsType<string>(dict["stdout"]);
        Assert.DoesNotContain("whole output", stdout, StringComparison.Ordinal);
        Assert.Contains("output_path holds the first 50000 bytes", stdout, StringComparison.Ordinal);
        var whole = await _fileSystem!.TryReadAllTextAsync(Assert.IsType<string>(dict["output_path"]), Ct);
        Assert.StartsWith(content[..49_000], whole, StringComparison.Ordinal);
        Assert.True(whole!.Length < 51_000, whole.Length.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    [Trait("BR", "CH-492")]
    public async Task ShouldNotClaimTheWholeOutput_WhenTheSessionKeptOnlyItsEnds()
    {
        var sandbox = new FakeShellSandbox
        {
            Configure = session => session.DefaultResult = new ShellRunResult(
                0, new string('h', 20_000), "", StdoutTail: new string('t', 20_000), StdoutOmitted: 1_000_000),
        };
        var tool = NewTool(sandbox);

        var dict = Assert.IsType<Dictionary<string, object?>>((await EchoAsync(tool)).Result);

        var stdout = Assert.IsType<string>(dict["stdout"]);
        Assert.DoesNotContain("whole output", stdout, StringComparison.Ordinal);
        Assert.Contains("output_path holds its first and last", stdout, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-492")]
    public async Task ShouldKeepOnlyTheMostRecentCaptureFiles()
    {
        var sandbox = new FakeShellSandbox
        {
            Configure = session => session.DefaultResult = new ShellRunResult(0, new string('h', 20_000), ""),
        };
        var tool = NewTool(sandbox, maxCaptureFiles: 2);

        var paths = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var dict = Assert.IsType<Dictionary<string, object?>>((await EchoAsync(tool)).Result);
            paths.Add(Assert.IsType<string>(dict["output_path"]));
        }

        Assert.Equal(2, ShellFiles().Length);
        Assert.False(await _fileSystem!.ExistsAsync(paths[0], Ct));
        Assert.True(await _fileSystem.ExistsAsync(paths[2], Ct));
    }

    [Fact]
    [Trait("BR", "CH-492")]
    public async Task ShouldRemoveTheCaptureFiles_WhenTheSessionFails()
    {
        var sandbox = new FakeShellSandbox
        {
            Configure = session =>
            {
                session.OnRun = invocation =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(invocation.CapturePath!)!);
                    File.WriteAllText(invocation.CapturePath!, "partial");
                    File.WriteAllText(invocation.CapturePath + ".o", "partial");
                };
                session.ThrowOnRun = new ShellSessionException("the shell died");
            },
        };
        var tool = NewTool(sandbox);

        await EchoAsync(tool);

        Assert.Empty(ShellFiles());
    }

    [Fact]
    [Trait("BR", "CH-492")]
    public async Task ShouldLeaveTheUsersLogFiles_WhenItPrunesTheFallbackDirectory()
    {
        var sandbox = new FakeShellSandbox
        {
            Configure = session => session.DefaultResult = new ShellRunResult(0, new string('h', 20_000), ""),
        };
        var tool = NewTool(sandbox, maxCaptureFiles: 1, withOutput: false);
        var shell = Path.Combine(Workspace, "shell");
        Directory.CreateDirectory(Path.Combine(shell, "deep"));
        var mine = Path.Combine(shell, "00000000000000000000-build.log");
        var deep = Path.Combine(shell, "deep", "000000000000000000000-0000aaaa.log");
        await File.WriteAllTextAsync(mine, "mine", Ct);
        await File.WriteAllTextAsync(deep, "mine too", Ct);

        string? last = null;
        for (var i = 0; i < 3; i++)
        {
            var dict = Assert.IsType<Dictionary<string, object?>>((await EchoAsync(tool)).Result);
            last = Assert.IsType<string>(dict["output_path"]);
            Assert.StartsWith("/workspace/shell/", last, StringComparison.Ordinal);
        }

        Assert.True(File.Exists(mine));
        Assert.True(File.Exists(deep));
        Assert.True(await _fileSystem!.ExistsAsync(last!, Ct));
        Assert.Equal(2, Directory.GetFiles(shell).Length);
    }

    [Fact]
    [Trait("BR", "CH-492")]
    public async Task ShouldKeepTheCaptureFileItReturns_WhenNewerOnesPassTheRetention()
    {
        var sandbox = new FakeShellSandbox
        {
            Configure = session =>
            {
                session.DefaultResult = new ShellRunResult(0, new string('h', 20_000), "");
                // Runs that ended while this one ran: their names sort after this one's.
                session.OnRun = invocation =>
                {
                    var directory = Path.GetDirectoryName(invocation.CapturePath!)!;
                    Directory.CreateDirectory(directory);
                    for (var i = 0; i < 3; i++)
                        File.WriteAllText(Path.Combine(directory, $"99999999999999999999{i}-0000000{i}.log"), "newer");
                };
            },
        };
        var tool = NewTool(sandbox, maxCaptureFiles: 2);

        var dict = Assert.IsType<Dictionary<string, object?>>((await EchoAsync(tool)).Result);

        var kept = Assert.IsType<string>(dict["output_path"]);
        Assert.True(await _fileSystem!.ExistsAsync(kept, Ct), "the capture file the result names was pruned");
        Assert.Equal(2, ShellFiles().Length);
    }

    [Fact]
    [Trait("BR", "CH-492")]
    public async Task ShouldNotShowAPhysicalPathTheSessionsCutSplit()
    {
        var physical = Path.Combine(Workspace, "big.txt");
        var split = physical.Length / 2;
        var sandbox = new FakeShellSandbox
        {
            Configure = session => session.DefaultResult = new ShellRunResult(
                0, "start " + physical[..split], "", StdoutTail: physical[split..] + " end", StdoutOmitted: 1_000_000),
        };
        var tool = NewTool(sandbox);

        var dict = Assert.IsType<Dictionary<string, object?>>((await EchoAsync(tool)).Result);

        var stdout = Assert.IsType<string>(dict["stdout"]);
        Assert.StartsWith("start ", stdout, StringComparison.Ordinal);
        Assert.EndsWith(" end", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain(physical[..split], stdout, StringComparison.Ordinal);
        Assert.DoesNotContain(physical[split..], stdout, StringComparison.Ordinal);
        var whole = await _fileSystem!.TryReadAllTextAsync(Assert.IsType<string>(dict["output_path"]), Ct);
        Assert.DoesNotContain(physical[..split], whole, StringComparison.Ordinal);
        Assert.DoesNotContain(physical[split..], whole, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("BR", "CH-492")]
    [InlineData(1 << 20)]
    [InlineData((1 << 20) + 7)]
    public async Task ShouldVirtualizeAPathThatStraddlesALongLinesCarry(int at)
    {
        Assert.SkipWhen(s_isWindows, "The POSIX session copies the output itself.");
        var tool = NewTool();
        var physical = Path.Combine(Workspace, "big.txt");
        var start = at - (physical.Length / 2);
        var content = new string('a', start - 1) + " " + physical + " " + new string('b', 1000) + "\n";

        var dict = await CatAsync(tool, content);

        var whole = await _fileSystem!.TryReadAllTextAsync(Assert.IsType<string>(dict["output_path"]), Ct);
        Assert.Contains(" /workspace/big.txt ", whole, StringComparison.Ordinal);
        Assert.DoesNotContain(Workspace, whole, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-492")]
    public async Task ShouldNotKeepAPhysicalPathTheCaptureCapSplit()
    {
        Assert.SkipWhen(s_isWindows, "The POSIX session copies the output itself.");
        var tool = NewTool(captureByteCap: 50_000);
        var physical = Path.Combine(Workspace, "big.txt");
        var split = physical.Length / 2;
        var content = new string('a', 50_000 - split - 1) + " " + physical + " " + Lines(100_000);

        var dict = await CatAsync(tool, content);

        var whole = await _fileSystem!.TryReadAllTextAsync(Assert.IsType<string>(dict["output_path"]), Ct);
        Assert.DoesNotContain(physical[..split], whole, StringComparison.Ordinal);
        Assert.EndsWith(" ", whole, StringComparison.Ordinal);
    }
}
