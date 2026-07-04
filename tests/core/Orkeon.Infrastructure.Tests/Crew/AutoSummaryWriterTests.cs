using System.Collections.Immutable;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Crew;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Infrastructure.Crew;

namespace Orkeon.Infrastructure.Tests.Crew;

/// <summary>
/// Validates that <see cref="AutoSummaryWriter"/> writes <c>AUTO_SUMMARY.md</c>
/// both on normal completion and on timeout / cancellation.
/// Uses Moq to stub <see cref="IFileSystemService"/> so no real disk I/O occurs,
/// except for the final write which targets a temp directory.
/// </summary>
public sealed class AutoSummaryWriterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _virtualOutputPath = "/output";
    private readonly IFileSystemService _fileSystem;

    public AutoSummaryWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"orkeon-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _fileSystem = BuildFileSystemStub(_tempDir, _virtualOutputPath);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static TempDirFs BuildFileSystemStub(string tempDir, string virtualOutputPath)
        => new TempDirFs(tempDir);

    /// <summary>
    /// IFileSystemService stub that maps any virtual path under <c>/output</c> to a temp dir
    /// and writes UTF-8 without BOM. Replaces the previous Moq setup.
    /// </summary>
    private sealed class TempDirFs : IFileSystemService
    {
        private readonly string _tempDir;
        private readonly UTF8Encoding _utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        public TempDirFs(string tempDir) { _tempDir = tempDir; }

        public Task<int> WriteAllTextAsync(string virtualPath, string content, CancellationToken ct)
        {
            var relative = virtualPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var physical = Path.Combine(_tempDir, relative);
            var dir = Path.GetDirectoryName(physical);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(physical, content, _utf8NoBom);
            return Task.FromResult(_utf8NoBom.GetByteCount(content));
        }

#pragma warning disable CS1998 // async without awaits
        public async IAsyncEnumerable<VirtualFileEntry> EnumerateFilesAsync(
            string virtualRoot, VirtualEnumerationOptions? options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        { yield break; }
#pragma warning restore CS1998

        public PathValidationResult ResolveAndValidate(string virtualPath, FileAccessRights requiredRight) => PathValidationResult.Allowed(virtualPath);
        public string? ToVirtualPath(string physicalPath) => physicalPath;
        public IReadOnlyList<MountInfo> GetAvailableMounts() => Array.Empty<MountInfo>();
        public Task<Stream> OpenReadStreamAsync(string virtualPath, CancellationToken ct) => throw new NotImplementedException();
        public Task<byte[]?> TryReadAllBytesAsync(string virtualPath, CancellationToken ct) => Task.FromResult<byte[]?>(null);
        public Task<string?> TryReadAllTextAsync(string virtualPath, CancellationToken ct) => Task.FromResult<string?>(null);
        public Task<VirtualEntryKind> GetEntryKindAsync(string virtualPath, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> ExistsAsync(string virtualPath, CancellationToken ct) => Task.FromResult(false);
        public Task CreateDirectoryAsync(string virtualPath, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> DeleteAsync(string virtualPath, bool recursive, CancellationToken ct) => Task.FromResult(false);
        public Task<int> WriteAllBytesAsync(string virtualPath, byte[] content, CancellationToken ct) => Task.FromResult(0);
        public Task<int> AppendAllTextAsync(string virtualPath, string content, CancellationToken ct) => Task.FromResult(0);
        public Task<VirtualFileEntry?> TryGetEntryAsync(string virtualPath, CancellationToken ct) => Task.FromResult<VirtualFileEntry?>(null);
        public Task<Stream> OpenWriteStreamAsync(string virtualPath, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Stream> OpenAppendStreamAsync(string virtualPath, CancellationToken ct = default) => throw new NotImplementedException();
        public Task CopyAsync(string srcVirtualPath, string dstVirtualPath, bool overwrite = false, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class DenyingFs : IFileSystemService
    {
        public Task<int> WriteAllTextAsync(string virtualPath, string content, CancellationToken ct)
            => throw new FileAccessDeniedException("access denied", virtualPath);

#pragma warning disable CS1998
        public async IAsyncEnumerable<VirtualFileEntry> EnumerateFilesAsync(
            string virtualRoot, VirtualEnumerationOptions? options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        { yield break; }
#pragma warning restore CS1998

        public PathValidationResult ResolveAndValidate(string virtualPath, FileAccessRights requiredRight) => PathValidationResult.Allowed(virtualPath);
        public string? ToVirtualPath(string physicalPath) => physicalPath;
        public IReadOnlyList<MountInfo> GetAvailableMounts() => Array.Empty<MountInfo>();
        public Task<Stream> OpenReadStreamAsync(string virtualPath, CancellationToken ct) => throw new NotImplementedException();
        public Task<byte[]?> TryReadAllBytesAsync(string virtualPath, CancellationToken ct) => Task.FromResult<byte[]?>(null);
        public Task<string?> TryReadAllTextAsync(string virtualPath, CancellationToken ct) => Task.FromResult<string?>(null);
        public Task<VirtualEntryKind> GetEntryKindAsync(string virtualPath, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> ExistsAsync(string virtualPath, CancellationToken ct) => Task.FromResult(false);
        public Task CreateDirectoryAsync(string virtualPath, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> DeleteAsync(string virtualPath, bool recursive, CancellationToken ct) => Task.FromResult(false);
        public Task<int> WriteAllBytesAsync(string virtualPath, byte[] content, CancellationToken ct) => Task.FromResult(0);
        public Task<int> AppendAllTextAsync(string virtualPath, string content, CancellationToken ct) => Task.FromResult(0);
        public Task<VirtualFileEntry?> TryGetEntryAsync(string virtualPath, CancellationToken ct) => Task.FromResult<VirtualFileEntry?>(null);
        public Task<Stream> OpenWriteStreamAsync(string virtualPath, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Stream> OpenAppendStreamAsync(string virtualPath, CancellationToken ct = default) => throw new NotImplementedException();
        public Task CopyAsync(string srcVirtualPath, string dstVirtualPath, bool overwrite = false, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static CrewExecutionSnapshot BuildSnapshot(
        CrewHookStatus status,
        int taskCount = 2,
        string? failureReason = null)
    {
        var tasks = Enumerable.Range(1, taskCount)
            .Select(i => new TaskExecutionSnapshot
            {
                TaskId = $"task-{i:D2}",
                AgentRole = $"agent-{i}",
                Success = status == CrewHookStatus.Completed,
                Duration = TimeSpan.FromSeconds(i * 5),
                CompletedAt = DateTimeOffset.UtcNow,
                ToolCallCount = i,
                TokensUsed = i * 100,
            })
            .ToImmutableList();

        return new CrewExecutionSnapshot
        {
            CrewId = "test-crew-id",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            EndedAt = DateTimeOffset.UtcNow,
            Tasks = tasks,
            Status = status,
            FailureReason = failureReason,
        };
    }

    private AutoSummaryWriter CreateWriter() =>
        new(_fileSystem, _virtualOutputPath, NullLogger<AutoSummaryWriter>.Instance);

    private string ExpectedPhysicalPath =>
        Path.Combine(_tempDir, "output", "AUTO_SUMMARY.md");

    // ── Tests ─────────────────────────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task OnCrewCompletedAsync_WritesAutoSummaryFile()
    {
        // Arrange
        var writer = CreateWriter();
        var snapshot = BuildSnapshot(CrewHookStatus.Completed, taskCount: 3);

        // Act
        await writer.OnCrewCompletedAsync(snapshot, CancellationToken.None);

        // Assert — file exists
        Assert.True(File.Exists(ExpectedPhysicalPath),
            $"Expected AUTO_SUMMARY.md at {ExpectedPhysicalPath}");

        var content = await File.ReadAllTextAsync(ExpectedPhysicalPath, TestContext.Current.CancellationToken);
        Assert.Contains("AUTO_SUMMARY", content);
        Assert.Contains("Completed", content);
        Assert.Contains("3", content); // task count
    }

    [Fact]
    public async System.Threading.Tasks.Task OnCrewFailedAsync_WritesAutoSummaryFileEvenOnCancellation()
    {
        // Arrange
        var writer = CreateWriter();
        var snapshot = BuildSnapshot(
            CrewHookStatus.Canceled,
            taskCount: 1,
            failureReason: "Crew execution was canceled (timeout or external cancellation).");

        // Act — simulate what SequentialProcessStrategy does on OperationCanceledException
        await writer.OnCrewFailedAsync(snapshot, null, CancellationToken.None);

        // Assert — file exists despite cancellation
        Assert.True(File.Exists(ExpectedPhysicalPath),
            $"Expected AUTO_SUMMARY.md at {ExpectedPhysicalPath} after cancellation");

        var content = await File.ReadAllTextAsync(ExpectedPhysicalPath, TestContext.Current.CancellationToken);
        Assert.Contains("AUTO_SUMMARY", content);
        Assert.Contains("Canceled", content);
        Assert.Contains("timeout", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async System.Threading.Tasks.Task OnCrewFailedAsync_ContainsTaskCounts()
    {
        // Arrange
        var writer = CreateWriter();
        var snapshot = BuildSnapshot(CrewHookStatus.Failed, taskCount: 4, failureReason: "Unexpected error");

        // Act
        await writer.OnCrewFailedAsync(snapshot, new InvalidOperationException("Unexpected error"), CancellationToken.None);

        // Assert
        Assert.True(File.Exists(ExpectedPhysicalPath));
        var content = await File.ReadAllTextAsync(ExpectedPhysicalPath, TestContext.Current.CancellationToken);

        // Task count information should be present
        Assert.Contains("task-01", content);
        Assert.Contains("task-04", content);
    }

    [Fact]
    public async System.Threading.Tasks.Task OnCrewCompletedAsync_WhenAccessDenied_DoesNotThrow()
    {
        // Arrange — stub that denies all writes
        var writer = new AutoSummaryWriter(
            new DenyingFs(), _virtualOutputPath, NullLogger<AutoSummaryWriter>.Instance);

        var snapshot = BuildSnapshot(CrewHookStatus.Completed);

        // Act & Assert — must not throw even when access is denied
        var exception = await Record.ExceptionAsync(
            () => writer.OnCrewCompletedAsync(snapshot, CancellationToken.None));
        Assert.Null(exception);
    }

    [Fact]
    public async System.Threading.Tasks.Task SimulatedCanceledCrew_HookIsCalledAndFileIsWritten()
    {
        // Arrange — set up a tiny snapshot accumulator to mimic what SequentialProcessStrategy does
        var writer = CreateWriter();
        var taskSnapshots = ImmutableList.Create(
            new TaskExecutionSnapshot
            {
                TaskId = "task-01",
                AgentRole = "researcher",
                Success = true,
                Duration = TimeSpan.FromSeconds(10),
                CompletedAt = DateTimeOffset.UtcNow,
            });

        var snapshot = new CrewExecutionSnapshot
        {
            CrewId = "simulated-crew",
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-15),
            EndedAt = DateTimeOffset.UtcNow,
            Tasks = taskSnapshots,
            Status = CrewHookStatus.Canceled,
            FailureReason = "Crew execution was canceled (timeout or external cancellation).",
        };

        // Act
        await writer.OnCrewFailedAsync(snapshot, null, CancellationToken.None);

        // Assert
        Assert.True(File.Exists(ExpectedPhysicalPath));
        var content = await File.ReadAllTextAsync(ExpectedPhysicalPath, TestContext.Current.CancellationToken);
        Assert.Contains("1", content); // 1 task completed
        Assert.Contains("researcher", content);
    }
}

