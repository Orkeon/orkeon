using Orkeon.Studio.Core.History;
using Orkeon.Studio.Core.Process;

namespace Orkeon.Studio.Core.Tests.History;

/// <summary>
/// The on-disk history. Tests use the real disk (the CLAUDE.md VFS exception for tests),
/// under a per-test temporary directory that stands in for the per-user config directory.
/// </summary>
public sealed class LaunchHistoryFileStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LaunchHistoryFileStore _store;

    public LaunchHistoryFileStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-studio-history-" + Guid.NewGuid().ToString("N"));
        _store = new LaunchHistoryFileStore(Path.Combine(_tempDir, LaunchHistoryFileStore.FileName));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static LaunchHistoryEntry Entry(string target) =>
        LaunchHistoryEntry.Starting(target, ["run", target], settingsPath: "/etc/orkeon/appsettings.json");

    [Fact]
    public async Task An_absent_file_reads_as_an_empty_history()
    {
        Assert.False(File.Exists(_store.FilePath));

        var history = await _store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Empty(history.Entries);
    }

    [Fact]
    public async Task A_recorded_launch_round_trips_through_the_file()
    {
        var entry = Entry("crew.yaml").WithResult(ProcessRunResult.FromExitCode(0, TimeSpan.FromSeconds(3)));

        await _store.RecordAsync(entry, TestContext.Current.CancellationToken);
        var reloaded = await _store.LoadAsync(TestContext.Current.CancellationToken);

        // The directory is created on write: on a fresh machine it does not exist yet.
        Assert.True(File.Exists(_store.FilePath));
        LaunchHistoryTests.AssertSameEntry(entry, Assert.Single(reloaded.Entries));
    }

    [Fact]
    public async Task Recording_keeps_the_newest_first_and_stays_bounded()
    {
        for (var i = 0; i < LaunchHistory.MaxEntries + 5; i++)
            await _store.RecordAsync(Entry($"crew-{i}.yaml"), TestContext.Current.CancellationToken);

        var history = await _store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LaunchHistory.MaxEntries, history.Entries.Count);
        Assert.Equal($"crew-{LaunchHistory.MaxEntries + 4}.yaml", history.Entries[0].Target);
    }

    [Fact]
    public async Task A_corrupt_file_reads_as_empty_rather_than_blocking_the_user()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(_store.FilePath, "{ this is not json", TestContext.Current.CancellationToken);

        var history = await _store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Empty(history.Entries);

        // And it must still be usable afterwards: the next launch overwrites it.
        await _store.RecordAsync(Entry("crew.yaml"), TestContext.Current.CancellationToken);
        Assert.Single((await _store.LoadAsync(TestContext.Current.CancellationToken)).Entries);
    }

    [Fact]
    public void The_default_location_sits_next_to_the_global_appsettings()
    {
        if (!LaunchHistoryFileStore.TryGetDefaultPath(out var path, out var error))
        {
            // A bare container without HOME: the failure has to be a message, not a crash.
            Assert.False(string.IsNullOrWhiteSpace(error));
            return;
        }

        Assert.NotNull(path);
        Assert.True(Path.IsPathRooted(path));
        Assert.Equal(LaunchHistoryFileStore.FileName, Path.GetFileName(path));
        Assert.Equal("Orkeon", Path.GetFileName(Path.GetDirectoryName(path)));
    }
}
