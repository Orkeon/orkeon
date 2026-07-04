using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Core.Watcher;

namespace Orkeon.Analysis.Tests;

public class CodebaseWatcherTests
{
    [Fact]
    public async Task Modified_file_emits_changed_event_after_debounce()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([("a.ts", "export class A {}")]);
        try
        {
            await using var watcher = new FileSystemWatcherCodebaseWatcher();
            var tcs = new TaskCompletionSource<CodebaseChangeNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
            watcher.Changed += (_, e) => tcs.TrySetResult(e);

            await watcher.StartAsync(new WatcherOptions
            {
                RootPath = dir,
                DebounceInterval = TimeSpan.FromMilliseconds(150),
            }, CancellationToken.None);

            await File.WriteAllTextAsync(Path.Combine(dir, "a.ts"), "export class A { v = 1; }", TestContext.Current.CancellationToken);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            cts.Token.Register(() => tcs.TrySetCanceled(cts.Token));
            var args = await tcs.Task;

            Assert.NotEmpty(args.Changes);
            Assert.Contains(args.Changes, c => c.RelativePath.EndsWith("a.ts", StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Burst_of_events_coalesces_into_single_batch()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([("a.ts", "x")]);
        try
        {
            await using var watcher = new FileSystemWatcherCodebaseWatcher();
            var batches = 0;
            var changes = new List<FileChange>();
            var batchTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            watcher.Changed += (_, e) =>
            {
                lock (changes) changes.AddRange(e.Changes);
                Interlocked.Increment(ref batches);
                batchTcs.TrySetResult();
            };

            await watcher.StartAsync(new WatcherOptions
            {
                RootPath = dir,
                DebounceInterval = TimeSpan.FromMilliseconds(250),
            }, CancellationToken.None);

            var aPath = Path.Combine(dir, "a.ts");
            for (var i = 0; i < 10; i++)
            {
                await File.WriteAllTextAsync(aPath, $"x{i}", TestContext.Current.CancellationToken);
                await Task.Delay(15, TestContext.Current.CancellationToken);
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            cts.Token.Register(() => batchTcs.TrySetCanceled(cts.Token));
            await batchTcs.Task;
            await Task.Delay(400, TestContext.Current.CancellationToken);

            // Coalescing is the property under test: 10 writes must NOT yield 10 batches.
            // Exactly 1 is the norm, but a sliding debounce cannot guarantee it when the
            // OS delivers a straggler AFTER the quiet window — on Windows, WriteAllText
            // emits several distinct FSW events and antivirus/indexer touches can flush a
            // late LastWrite event, legitimately opening a second batch. Tolerate that
            // single straggler batch; anything more means the debounce is broken.
            Assert.InRange(batches, 1, 2);
            lock (changes)
            {
                Assert.All(changes, c => Assert.EndsWith("a.ts", c.RelativePath, StringComparison.OrdinalIgnoreCase));
            }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Excluded_directories_are_ignored()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("src/a.ts", "x"),
            ("node_modules/lib.ts", "y"),
        ]);
        try
        {
            await using var watcher = new FileSystemWatcherCodebaseWatcher();
            var received = new List<FileChange>();
            var batchTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            watcher.Changed += (_, e) =>
            {
                received.AddRange(e.Changes);
                batchTcs.TrySetResult();
            };

            await watcher.StartAsync(new WatcherOptions
            {
                RootPath = dir,
                DebounceInterval = TimeSpan.FromMilliseconds(150),
                ExcludePatterns = ["node_modules"],
            }, CancellationToken.None);

            await File.WriteAllTextAsync(Path.Combine(dir, "node_modules", "lib.ts"), "z", TestContext.Current.CancellationToken);
            await Task.Delay(80, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(dir, "src", "a.ts"), "z", TestContext.Current.CancellationToken);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            cts.Token.Register(() => batchTcs.TrySetCanceled(cts.Token));
            await batchTcs.Task;
            await Task.Delay(300, TestContext.Current.CancellationToken);

            Assert.DoesNotContain(received, c => c.RelativePath.Contains("node_modules", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(received, c => c.RelativePath.Contains("a.ts", StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task DisposeAsync_stops_the_watcher()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([("a.ts", "x")]);
        try
        {
            var watcher = new FileSystemWatcherCodebaseWatcher();
            var events = 0;
            watcher.Changed += (_, _) => Interlocked.Increment(ref events);

            await watcher.StartAsync(new WatcherOptions
            {
                RootPath = dir,
                DebounceInterval = TimeSpan.FromMilliseconds(150),
            }, CancellationToken.None);

            await watcher.DisposeAsync();

            await File.WriteAllTextAsync(Path.Combine(dir, "a.ts"), "y", TestContext.Current.CancellationToken);
            await Task.Delay(400, TestContext.Current.CancellationToken);

            Assert.Equal(0, events);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Starting_twice_throws()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([("a.ts", "x")]);
        try
        {
            await using var watcher = new FileSystemWatcherCodebaseWatcher();
            await watcher.StartAsync(new WatcherOptions { RootPath = dir }, CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                watcher.StartAsync(new WatcherOptions { RootPath = dir }, CancellationToken.None));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}

public class RaggableTreeEventBusTests
{
    [Fact]
    public async Task Publish_invokes_all_subscribers()
    {
        var bus = new InMemoryRaggableTreeEventBus();
        var count = 0;
        var tcs1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var s1 = bus.Subscribe((_, _) => { Interlocked.Increment(ref count); tcs1.TrySetResult(); return Task.CompletedTask; });
        using var s2 = bus.Subscribe((_, _) => { Interlocked.Increment(ref count); tcs2.TrySetResult(); return Task.CompletedTask; });

        bus.Publish(new RaggableTreeUpdated("old", "new", [], [], []));

        await Task.WhenAll(tcs1.Task, tcs2.Task).WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Unsubscribed_handler_is_not_invoked()
    {
        var bus = new InMemoryRaggableTreeEventBus();
        var count = 0;
        var subscription = bus.Subscribe((_, _) => { Interlocked.Increment(ref count); return Task.CompletedTask; });
        subscription.Dispose();

        bus.Publish(new RaggableTreeUpdated("a", "b", [], [], []));
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Faulting_subscriber_does_not_prevent_others()
    {
        var bus = new InMemoryRaggableTreeEventBus();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var sFault = bus.Subscribe((_, _) => throw new InvalidOperationException("boom"));
        using var sOk = bus.Subscribe((_, _) => { tcs.TrySetResult(); return Task.CompletedTask; });

        bus.Publish(new RaggableTreeUpdated("a", "b", [], [], []));
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.True(tcs.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Event_payload_carries_fqn_diffs()
    {
        var bus = new InMemoryRaggableTreeEventBus();
        RaggableTreeUpdated? received = null;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var _ = bus.Subscribe((e, _) => { received = e; tcs.TrySetResult(); return Task.CompletedTask; });

        var evt = new RaggableTreeUpdated(
            "prev",
            "next",
            ImmutableArray.Create("pkg::A"),
            ImmutableArray.Create("pkg::B"),
            ImmutableArray.Create("pkg::C"));
        bus.Publish(evt);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.NotNull(received);
        Assert.Equal("prev", received!.PreviousIndexId);
        Assert.Equal("next", received.NewIndexId);
        Assert.Equal("pkg::A", Assert.Single(received.AddedFqns));
        Assert.Equal("pkg::B", Assert.Single(received.RemovedFqns));
        Assert.Equal("pkg::C", Assert.Single(received.ModifiedFqns));
    }
}
