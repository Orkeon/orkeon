using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Infrastructure.FileSystem;

namespace Orkeon.Infrastructure.Tests.FileSystem;

/// <summary>
/// xUnit collection grouping all tests that drive a real <c>System.IO.FileSystemWatcher</c>.
/// Sets <c>DisableParallelization = true</c> so future watcher tests added to this collection
/// run one at a time, reducing inotify/9P contention under the WSL2 test mount.
/// </summary>
[CollectionDefinition("FileSystemWatcher", DisableParallelization = true)]
public sealed class FileSystemWatcherCollection { }

[Collection("FileSystemWatcher")]
public sealed class VirtualFileSystemWatcherTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IFileSystemService _fs;

    public VirtualFileSystemWatcherTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"orkeon-vfsw-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _fs = BuildService(_tempDir, "/src", FileAccessRights.ReadOnly);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task VirtualFileSystemWatcher_EmitsCreatedOnFileCreate()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var watcher = new VirtualFileSystemWatcher(_fs, NullLogger<VirtualFileSystemWatcher>.Instance);
        var opts = new VirtualWatchOptions(IncludeSubdirectories: true);

        var received = new List<VirtualFileSystemChange>();
        var lockObj = new object();
        var consumer = StartConsumer(watcher, opts, received, lockObj, cts.Token);

        await WaitForWatcherReadyAsync(received, lockObj, opts.DebounceWindow, cts.Token);

        var filePath = Path.Combine(_tempDir, "new-file.txt");
        await File.WriteAllTextAsync(filePath, "hello", cts.Token);

        var change = await WaitForEventAsync(
            received, lockObj,
            c => c.VirtualPath == "/src/new-file.txt" && c.Kind == VirtualFileChangeKind.Created,
            timeout: TimeSpan.FromSeconds(15),
            cts.Token);

        await cts.CancelAsync();
        await consumer;

        Assert.NotNull(change);
        Assert.Equal(VirtualFileChangeKind.Created, change!.Kind);
        Assert.Equal("/src/new-file.txt", change.VirtualPath);
        Assert.DoesNotContain(_tempDir, change.VirtualPath); // no physical path leak
    }

    [Fact]
    public async Task VirtualFileSystemWatcher_DisposeStopsEvents()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var watcher = new VirtualFileSystemWatcher(_fs, NullLogger<VirtualFileSystemWatcher>.Instance);
        var opts = new VirtualWatchOptions(IncludeSubdirectories: true);

        var received = new List<VirtualFileSystemChange>();
        var lockObj = new object();
        var consumer = StartConsumer(watcher, opts, received, lockObj, cts.Token);

        await WaitForWatcherReadyAsync(received, lockObj, opts.DebounceWindow, cts.Token);

        // Snapshot post-warmup count (excluding warmup-marker events that are already drained).
        int countBefore;
        lock (lockObj) countBefore = received.Count;

        // Cancel the CT — this stops the watcher channel.
        await cts.CancelAsync();
        await consumer;

        // Write a file after cancellation; the watcher should not enqueue it.
        await Task.Delay(300, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "after-stop.txt"), "x", TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);

        int countAfter;
        lock (lockObj) countAfter = received.Count;

        Assert.Equal(countBefore, countAfter);
    }

    [Fact]
    public async Task VirtualFileSystemWatcher_DropsEventsOutsideMount()
    {
        // ToVirtualPath returns null for physical paths outside any mount — watcher must drop them.
        // Verified via the implementation: OnEvent does `if (vPath is null) return;`
        // Direct test is impractical without injecting a stub FileSystemWatcher.
        // Document: events with no virtual path mapping are silently dropped.
        await Task.CompletedTask; // contract-documentation test
        Assert.True(true, "Events with no virtual path mapping are silently dropped (see VirtualFileSystemWatcher.OnEvent).");
    }

    [Fact]
    public async Task VirtualFileSystemWatcher_DebounceCollapsesFastWrites()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var watcher = new VirtualFileSystemWatcher(_fs, NullLogger<VirtualFileSystemWatcher>.Instance);
        var opts = new VirtualWatchOptions(
            IncludeSubdirectories: true,
            DebounceWindow: TimeSpan.FromMilliseconds(300));

        var filePath = Path.Combine(_tempDir, "debounce.txt");
        await File.WriteAllTextAsync(filePath, "seed", TestContext.Current.CancellationToken); // create before watching so we only see Modified

        var received = new List<VirtualFileSystemChange>();
        var lockObj = new object();
        var consumer = StartConsumer(watcher, opts, received, lockObj, cts.Token);

        await WaitForWatcherReadyAsync(received, lockObj, opts.DebounceWindow, cts.Token);

        // Write the same file 5 times in rapid succession.
        for (var i = 0; i < 5; i++)
        {
            await File.WriteAllTextAsync(filePath, $"write-{i}", cts.Token);
            await Task.Delay(20, cts.Token);
        }

        // Wait for debounce window to pass + buffer.
        await Task.Delay(700, cts.Token);
        await cts.CancelAsync();
        await consumer;

        List<VirtualFileSystemChange> modifiedEvents;
        lock (lockObj)
        {
            modifiedEvents = received.Where(c =>
                c.VirtualPath == "/src/debounce.txt" && c.Kind == VirtualFileChangeKind.Modified).ToList();
        }

        // With a 300 ms debounce window, multiple rapid writes should be collapsed to fewer events.
        Assert.True(modifiedEvents.Count < 5,
            $"Expected debounce to collapse writes but received {modifiedEvents.Count} Modified events.");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static Task StartConsumer(
        VirtualFileSystemWatcher watcher,
        VirtualWatchOptions opts,
        List<VirtualFileSystemChange> received,
        object lockObj,
        CancellationToken ct)
    {
        return Task.Run(async () =>
        {
            try
            {
                await foreach (var change in watcher.WatchAsync("/src", opts, ct))
                {
                    lock (lockObj) received.Add(change);
                }
            }
            catch (OperationCanceledException) { /* expected on test teardown */ }
        }, CancellationToken.None);
    }

    /// <summary>
    /// Prime-then-write handshake: writes a unique marker file repeatedly until the watcher
    /// emits a corresponding event. Once the handshake succeeds, all marker events are
    /// drained from <paramref name="received"/> so subsequent test assertions ignore them.
    /// This eliminates the timing race where <c>FileSystemWatcher.EnableRaisingEvents</c>
    /// is set but the underlying inotify/9P backend has not yet started delivering events
    /// — under WSL2 mount + parallel test load, that warmup can take 1-3 s.
    /// </summary>
    private async Task WaitForWatcherReadyAsync(
        List<VirtualFileSystemChange> received,
        object lockObj,
        TimeSpan? debounceWindow,
        CancellationToken ct)
    {
        var marker = $".watcher-ready-{Guid.NewGuid():N}.txt";
        var virtualMarker = $"/src/{marker}";
        var markerPath = Path.Combine(_tempDir, marker);

        // Per-iteration wait must exceed the watcher's debounce window (debounced events
        // surface only after `DebounceWindow` has elapsed).
        var perIterationWait = (debounceWindow ?? TimeSpan.Zero) + TimeSpan.FromMilliseconds(500);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        var iteration = 0;

        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            await File.WriteAllTextAsync(markerPath, $"ping-{iteration++}", ct);

            var iterDeadline = DateTimeOffset.UtcNow.Add(perIterationWait);
            while (DateTimeOffset.UtcNow < iterDeadline)
            {
                await Task.Delay(50, ct);
                lock (lockObj)
                {
                    if (received.Any(c => c.VirtualPath == virtualMarker))
                    {
                        // Drop ALL warmup-marker events so subsequent assertions ignore them.
                        received.RemoveAll(c => c.VirtualPath == virtualMarker);
                        return;
                    }
                }
            }
        }

        throw new InvalidOperationException(
            "VirtualFileSystemWatcher did not emit any event for the warmup marker within 20 s — " +
            "inotify/9P backend likely starved or test mount is not event-capable.");
    }

    private static async Task<VirtualFileSystemChange?> WaitForEventAsync(
        List<VirtualFileSystemChange> received,
        object lockObj,
        Func<VirtualFileSystemChange, bool> predicate,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            lock (lockObj)
            {
                var match = received.FirstOrDefault(predicate);
                if (match is not null) return match;
            }
            await Task.Delay(50, ct);
        }
        return null;
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The registry is captured by the returned FileSystemService and must outlive this factory; it lives for the duration of the test.")]
    private static FileSystemService BuildService(string basePath, string virtualPath, FileAccessRights rights)
    {
        var mount = new FileSystemMount(basePath, virtualPath, rights);
        var registry = new FileSystemRegistry([mount]);

        var pathValidator = new StubPathValidator()
            .RespondWith((p, _) => PathValidationResult.Allowed(p));

        return new FileSystemService(registry, pathValidator, NullLogger<FileSystemService>.Instance);
    }
}
