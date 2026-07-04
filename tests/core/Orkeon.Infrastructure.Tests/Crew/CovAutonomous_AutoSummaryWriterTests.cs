using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Crew;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Infrastructure.Crew;

namespace Orkeon.Infrastructure.Tests.CovAutonomous;

/// <summary>
/// Branch-coverage tests for <see cref="AutoSummaryWriter"/> that exercise the
/// output-file enumeration, cache-stat, and FQN-warning markdown sections.
/// Uses an in-memory capturing <see cref="IFileSystemService"/> (no disk).
/// </summary>
public sealed class CovAutonomous_AutoSummaryWriterTests
{
    private const string OutputMount = "/output";

    // ── Capturing file system double ────────────────────────────────────────

    private sealed class CapturingFs : IFileSystemService
    {
        private readonly IReadOnlyList<VirtualFileEntry> _entries;
        private readonly bool _enumerationThrows;

        public CapturingFs(
            IReadOnlyList<VirtualFileEntry>? entries = null,
            bool enumerationThrows = false)
        {
            _entries = entries ?? [];
            _enumerationThrows = enumerationThrows;
        }

        public string? LastWrittenPath { get; private set; }
        public string? LastWrittenContent { get; private set; }

        public Task<int> WriteAllTextAsync(string virtualPath, string content, CancellationToken ct)
        {
            LastWrittenPath = virtualPath;
            LastWrittenContent = content;
            return Task.FromResult(content.Length);
        }

        public async IAsyncEnumerable<VirtualFileEntry> EnumerateFilesAsync(
            string virtualRoot, VirtualEnumerationOptions? options,
            [EnumeratorCancellation] CancellationToken ct)
        {
            if (_enumerationThrows)
                throw new InvalidOperationException("enumeration boom");
            foreach (var e in _entries)
            {
                ct.ThrowIfCancellationRequested();
                yield return e;
                await Task.Yield();
            }
        }

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

    private static AutoSummaryWriter CreateWriter(IFileSystemService fs) =>
        new(fs, OutputMount, NullLogger<AutoSummaryWriter>.Instance);

    private static VirtualFileEntry File(string path, long size) =>
        new(path, size, DateTimeOffset.UtcNow, VirtualEntryKind.File);

    private static VirtualFileEntry Dir(string path) =>
        new(path, 0, DateTimeOffset.UtcNow, VirtualEntryKind.Directory);

    // ── Constructor guards ────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullFileSystem_Throws()
        => Assert.Throws<ArgumentNullException>(() =>
            new AutoSummaryWriter(null!, OutputMount, NullLogger<AutoSummaryWriter>.Instance));

    [Fact]
    public void Constructor_NullLogger_Throws()
        => Assert.Throws<ArgumentNullException>(() =>
            new AutoSummaryWriter(new CapturingFs(), OutputMount, null!));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankOutputMount_Throws(string mount)
        => Assert.Throws<ArgumentException>(() =>
            new AutoSummaryWriter(new CapturingFs(), mount, NullLogger<AutoSummaryWriter>.Instance));

    [Fact]
    public void Constructor_NullOutputMount_Throws()
        => Assert.Throws<ArgumentNullException>(() =>
            new AutoSummaryWriter(new CapturingFs(), null!, NullLogger<AutoSummaryWriter>.Instance));

    // ── OnTaskCompletedAsync is a no-op ────────────────────────────────────

    [Fact]
    public async Task OnTaskCompletedAsync_DoesNotWriteAnything()
    {
        var fs = new CapturingFs();
        var writer = CreateWriter(fs);

        await writer.OnTaskCompletedAsync(
            new TaskExecutionSnapshot
            {
                TaskId = "t",
                AgentRole = "r",
                Success = true,
                Duration = TimeSpan.Zero,
                CompletedAt = DateTimeOffset.UtcNow,
            },
            CancellationToken.None);

        Assert.Null(fs.LastWrittenContent);
    }

    // ── Output files section ────────────────────────────────────────────────

    [Fact]
    public async Task OnCrewCompletedAsync_WithOutputFiles_ListsThemAndExcludesSummary()
    {
        var fs = new CapturingFs(
        [
            File("/output/report.md", 1234),
            Dir("/output/sub"),                      // directories are skipped
            File("/output/sub/data.json", 99),
            File("/output/AUTO_SUMMARY.md", 10),     // the summary itself is excluded
        ]);
        var writer = CreateWriter(fs);

        await writer.OnCrewCompletedAsync(
            BuildSnapshot(CrewHookStatus.Completed), CancellationToken.None);

        var content = fs.LastWrittenContent!;
        Assert.Contains("## Output files", content);
        Assert.Contains("/output/report.md", content);
        Assert.Contains("1,234 bytes", content);
        Assert.Contains("/output/sub/data.json", content);
        Assert.DoesNotContain("| /output/AUTO_SUMMARY.md |", content);
        Assert.DoesNotContain("/output/sub |", content); // dir not listed as a file row
    }

    [Fact]
    public async Task OnCrewCompletedAsync_NoOutputFiles_ShowsPlaceholder()
    {
        var fs = new CapturingFs();
        var writer = CreateWriter(fs);

        await writer.OnCrewCompletedAsync(
            BuildSnapshot(CrewHookStatus.Completed), CancellationToken.None);

        Assert.Contains("_(no files found in output mount)_", fs.LastWrittenContent!);
    }

    [Fact]
    public async Task OnCrewCompletedAsync_EnumerationThrows_StillWritesSummary()
    {
        var fs = new CapturingFs(enumerationThrows: true);
        var writer = CreateWriter(fs);

        await writer.OnCrewCompletedAsync(
            BuildSnapshot(CrewHookStatus.Completed), CancellationToken.None);

        // CollectOutputFilesAsync swallows the error; summary is still produced.
        Assert.NotNull(fs.LastWrittenContent);
        Assert.Contains("_(no files found in output mount)_", fs.LastWrittenContent!);
    }

    // ── Empty tasks ───────────────────────────────────────────────────────

    [Fact]
    public async Task OnCrewCompletedAsync_NoTasks_ShowsTaskPlaceholder()
    {
        var fs = new CapturingFs();
        var writer = CreateWriter(fs);

        var snapshot = new CrewExecutionSnapshot
        {
            CrewId = "c",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            EndedAt = DateTimeOffset.UtcNow,
            Tasks = ImmutableList<TaskExecutionSnapshot>.Empty,
            Status = CrewHookStatus.Completed,
        };

        await writer.OnCrewCompletedAsync(snapshot, CancellationToken.None);

        Assert.Contains("_(no tasks recorded)_", fs.LastWrittenContent!);
    }

    // ── Cache stats ─────────────────────────────────────────────────────────

    [Fact]
    public async Task OnCrewCompletedAsync_WithCacheTokens_RendersCacheStatsAndPerTaskCells()
    {
        var fs = new CapturingFs();
        var writer = CreateWriter(fs);

        var tasks = ImmutableList.Create(
            new TaskExecutionSnapshot
            {
                TaskId = "t1",
                AgentRole = "writer",
                Success = true,
                Duration = TimeSpan.FromSeconds(3),
                CompletedAt = DateTimeOffset.UtcNow,
                TokensUsed = 500,
                CacheHitTokens = 300,
                CacheMissTokens = 200,
            });

        var snapshot = new CrewExecutionSnapshot
        {
            CrewId = "c",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            EndedAt = DateTimeOffset.UtcNow,
            Tasks = tasks,
            Status = CrewHookStatus.Completed,
        };

        await writer.OnCrewCompletedAsync(snapshot, CancellationToken.None);

        var content = fs.LastWrittenContent!;
        Assert.Contains("**Prompt cache**", content);
        Assert.Contains("hit 300", content);
        Assert.Contains("miss 200", content);
        Assert.Contains("500 · 300/200", content); // per-task tokens cell with cache
    }

    [Fact]
    public async Task OnCrewCompletedAsync_NoCacheTokens_OmitsCacheStats()
    {
        var fs = new CapturingFs();
        var writer = CreateWriter(fs);

        await writer.OnCrewCompletedAsync(
            BuildSnapshot(CrewHookStatus.Completed), CancellationToken.None);

        Assert.DoesNotContain("**Prompt cache**", fs.LastWrittenContent!);
    }

    // ── FQN warning sections ─────────────────────────────────────────────────

    [Fact]
    public async Task OnCrewCompletedAsync_WithFqnWarnings_RendersAllThreeSections()
    {
        var fs = new CapturingFs();
        var writer = CreateWriter(fs);

        var task = new TaskExecutionSnapshot
        {
            TaskId = "t1",
            AgentRole = "analyst",
            Success = true,
            Duration = TimeSpan.FromSeconds(1),
            CompletedAt = DateTimeOffset.UtcNow,
            UnknownFqns = ["cs::Foo.Bar"],
            RewrittenFqns = ImmutableDictionary<string, string>.Empty.Add("ts::Sym", "ts::pkg::Sym"),
            AmbiguousFqns =
            [
                new TaskAmbiguousFqn("py::Thing", ["a::Thing", "b::Thing"]),
            ],
        };

        var snapshot = new CrewExecutionSnapshot
        {
            CrewId = "c",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            EndedAt = DateTimeOffset.UtcNow,
            Tasks = ImmutableList.Create(task),
            Status = CrewHookStatus.Completed,
        };

        await writer.OnCrewCompletedAsync(snapshot, CancellationToken.None);

        var content = fs.LastWrittenContent!;
        Assert.Contains("## Warnings — Unknown FQNs", content);
        Assert.Contains("`cs::Foo.Bar`", content);
        Assert.Contains("## Notes — Auto-rewritten bare FQNs", content);
        Assert.Contains("`ts::Sym` → `ts::pkg::Sym`", content);
        Assert.Contains("## Warnings — Ambiguous bare FQNs", content);
        Assert.Contains("`py::Thing`", content);
        Assert.Contains("`a::Thing`", content);
    }

    [Fact]
    public async Task OnCrewFailedAsync_WithFailureReason_RendersFailureReasonLine()
    {
        var fs = new CapturingFs();
        var writer = CreateWriter(fs);

        var snapshot = BuildSnapshot(CrewHookStatus.Failed, failureReason: "kaboom happened");

        await writer.OnCrewFailedAsync(
            snapshot, new InvalidOperationException("kaboom happened"), CancellationToken.None);

        var content = fs.LastWrittenContent!;
        Assert.Contains("**Failure reason**: kaboom happened", content);
        Assert.Contains("**Status**: Failed", content);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static CrewExecutionSnapshot BuildSnapshot(
        CrewHookStatus status, string? failureReason = null)
    {
        var tasks = ImmutableList.Create(
            new TaskExecutionSnapshot
            {
                TaskId = "task-01",
                AgentRole = "agent-1",
                Success = status == CrewHookStatus.Completed,
                Duration = TimeSpan.FromSeconds(5),
                CompletedAt = DateTimeOffset.UtcNow,
                ToolCallCount = 2,
                TokensUsed = 100,
            });

        return new CrewExecutionSnapshot
        {
            CrewId = "crew",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            EndedAt = DateTimeOffset.UtcNow,
            Tasks = tasks,
            Status = status,
            FailureReason = failureReason,
        };
    }
}
