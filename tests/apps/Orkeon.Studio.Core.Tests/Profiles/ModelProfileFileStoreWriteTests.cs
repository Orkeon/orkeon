using Orkeon.Studio.Core.Profiles;

namespace Orkeon.Studio.Core.Tests.Profiles;

/// <summary>
/// STUDIO-56, decision 3: the model profiles file is replaced whole or not at all, and a write the
/// disk refuses is no longer swallowed by the store — the Models tab says it. The store wrote the file
/// in place (<c>File.WriteAllTextAsync</c> empties it first) and kept an I/O failure to itself.
/// </summary>
public sealed class ModelProfileFileStoreWriteTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "orkeon-profile-writes-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private static ModelProfileSet Set(string name, double? temperature = null) => ModelProfileSet.Empty.Upsert(
        new ModelProfile { Name = name, Provider = "ollama", Model = "qwen2.5:14b", BaseUrl = "http://localhost:11434/v1", Temperature = temperature });

    [Fact]
    public async Task A_write_the_disk_refuses_reaches_the_caller()
    {
        // The parent "directory" is a file: no write can land, whatever the platform.
        var blocker = Path.Combine(_root, "not-a-directory");
        await File.WriteAllTextAsync(blocker, "", TestContext.Current.CancellationToken);
        var store = new ModelProfileFileStore(Path.Combine(blocker, ModelProfileFileStore.FileName));

        await Assert.ThrowsAnyAsync<IOException>(() => store.SaveAsync(Set("Local"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_set_that_cannot_be_written_leaves_the_file_as_it_was()
    {
        var path = Path.Combine(_root, ModelProfileFileStore.FileName);
        var store = new ModelProfileFileStore(path);
        await store.SaveAsync(Set("Local"), TestContext.Current.CancellationToken);
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        // JSON has no infinity: the serializer refuses the set.
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.SaveAsync(Set("Hot", double.PositiveInfinity), TestContext.Current.CancellationToken));

        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal([ModelProfileFileStore.FileName], Directory.GetFiles(_root).Select(Path.GetFileName));
    }

    [Fact]
    public async Task A_write_leaves_no_file_beside_the_profiles_file()
    {
        var path = Path.Combine(_root, ModelProfileFileStore.FileName);
        var store = new ModelProfileFileStore(path);

        await store.SaveAsync(Set("Local"), TestContext.Current.CancellationToken);
        await store.SaveAsync(Set("Remote"), TestContext.Current.CancellationToken);

        Assert.Equal([ModelProfileFileStore.FileName], Directory.GetFiles(_root).Select(Path.GetFileName));
        var loaded = await store.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Remote", Assert.Single(loaded.Set.Profiles).Name);
    }

    [Fact]
    public async Task A_write_refused_midway_leaves_the_file_as_it_was_and_no_staging_file()
    {
        var path = Path.Combine(_root, ModelProfileFileStore.FileName);
        var store = new ModelProfileFileStore(path);
        await store.SaveAsync(Set("Local"), TestContext.Current.CancellationToken);
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        // A cancelled write stops before the replacement: the original stays whole.
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(Set("Remote"), cancelled.Token));

        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal([ModelProfileFileStore.FileName], Directory.GetFiles(_root).Select(Path.GetFileName));
    }
}
