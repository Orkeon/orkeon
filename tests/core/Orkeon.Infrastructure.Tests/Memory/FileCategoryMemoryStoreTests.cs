using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.Memory;
using Orkeon.Infrastructure.Session;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Memory;

/// <summary>The file-backed typed memory of <c>memory_store</c> (#286): one memory.json per project, under the session's project slug.</summary>
public sealed class FileCategoryMemoryStoreTests
{
    private const string Root = "/workspace/.orkeon/memory";
    private const string Sessions = "/workspace/.orkeon/sessions";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FakeFileSystemService NewFs() =>
        new FakeFileSystemService().AddMount("/workspace", FileAccessRights.ReadWrite);

    private static FileCategoryMemoryStore NewStore(IFileSystemService fs) => new(fs, Root);

    [Fact]
    public async Task ShouldListWhatWasAdded_AndBeReadByANewStoreOverTheSameDirectory()
    {
        var fs = NewFs();
        var store = NewStore(fs);
        var id1 = await store.AddAsync("project", "uses pnpm", Ct);
        var id2 = await store.AddAsync("user", "prefers strict TS", Ct);

        var listed = await store.ListAsync(null, false, Ct);
        Assert.Equal([id1, id2], listed.Select(e => e.Id));
        Assert.All(listed, e => Assert.False(string.IsNullOrWhiteSpace(e.CreatedAt)));

        var reopened = NewStore(fs);
        var again = await reopened.ListAsync("project", false, Ct);
        var only = Assert.Single(again);
        Assert.Equal(id1, only.Id);
        Assert.Equal("uses pnpm", only.Content);
        Assert.Equal(listed[0].CreatedAt, only.CreatedAt);
        Assert.Equal("prefers strict TS", (await reopened.GetAsync(id2, Ct))!.Content);
    }

    [Fact]
    public async Task ShouldKeepTheSessionProjectSlug_InItsPath()
    {
        var fs = NewFs();
        var session = new FileSessionBufferService(fs, new CliSessionOptions { SessionsDirectory = Sessions });
        await session.LoadAsync("/workspace", Ct);
        var sessionFolder = session.SessionFilePath![$"{Sessions}/".Length..].Split('/')[0];

        var store = NewStore(fs);
        await store.AddAsync("project", "x", Ct);

        Assert.Equal($"{Root}/{ProjectKey.Slug("/workspace")}/memory.json", store.FilePath);
        Assert.Equal(sessionFolder, ProjectKey.Slug("/workspace"));
        Assert.True(await fs.ExistsAsync(store.FilePath, Ct));
    }

    [Fact]
    public async Task ShouldKeepIdAndCategory_AndRefreshCreatedAt_WhenReplacing()
    {
        var fs = NewFs();
        var store = NewStore(fs);
        var id = await store.AddAsync("feedback", "run the linter", Ct);
        var before = (await store.GetAsync(id, Ct))!;

        Assert.True(await store.ReplaceAsync(id, "run the linter and the tests", Ct));
        Assert.False(await store.ReplaceAsync(id + 1, "nobody", Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReplaceAsync(id, " ", Ct));

        var after = (await NewStore(fs).GetAsync(id, Ct))!;
        Assert.Equal(id, after.Id);
        Assert.Equal("feedback", after.Category);
        Assert.Equal("run the linter and the tests", after.Content);
        Assert.True(string.CompareOrdinal(after.CreatedAt, before.CreatedAt) >= 0);
        Assert.Single(await NewStore(fs).ListAsync(null, false, Ct));
    }

    [Fact]
    public async Task ShouldNeverReuseAnId_AfterADelete_EvenAcrossInstances()
    {
        var fs = NewFs();
        var store = NewStore(fs);
        var id1 = await store.AddAsync("project", "a", Ct);
        var id2 = await store.AddAsync("project", "b", Ct);
        Assert.True(await store.DeleteAsync(id2, Ct));
        Assert.False(await store.DeleteAsync(id2, Ct));

        var id3 = await store.AddAsync("project", "c", Ct);
        Assert.True(id3 > id2);

        Assert.True(await NewStore(fs).DeleteAsync(id3, Ct));
        var id4 = await NewStore(fs).AddAsync("project", "d", Ct);
        Assert.True(id4 > id3);
        Assert.Equal([id1, id4], (await NewStore(fs).ListAsync(null, false, Ct)).Select(e => e.Id));
    }

    [Fact]
    public async Task ShouldBeEmpty_WhenTheFileIsMissingOrEmpty()
    {
        var fs = NewFs();
        Assert.Empty(await NewStore(fs).ListAsync(null, false, Ct));
        Assert.Null(await NewStore(fs).GetAsync(1, Ct));
        Assert.False(await NewStore(fs).DeleteAsync(1, Ct));

        var store = NewStore(fs);
        await fs.WriteAllTextAsync(store.FilePath, "", Ct);
        Assert.Empty(await store.ListAsync(null, false, Ct));
        Assert.Equal(1, await store.AddAsync("user", "first", Ct));
    }

    [Fact]
    public async Task ShouldOrderNewestFirst_ByCreatedAtThenId()
    {
        var fs = NewFs();
        var store = NewStore(fs);
        var id1 = await store.AddAsync("project", "first", Ct);
        var id2 = await store.AddAsync("project", "second", Ct);
        var id3 = await store.AddAsync("user", "third", Ct);

        Assert.True(await store.ReplaceAsync(id1, "first, revised", Ct));
        var newest = await NewStore(fs).ListAsync(null, true, Ct);
        Assert.Equal(id1, newest[0].Id);
        Assert.Equal([id3, id2], newest.Skip(1).Select(e => e.Id));

        var projectOnly = await NewStore(fs).ListAsync("project", true, Ct);
        Assert.Equal([id1, id2], projectOnly.Select(e => e.Id));
    }

    [Fact]
    public async Task ShouldRefuseUnreadableFile_InsteadOfOverwriting()
    {
        var fs = NewFs();
        var store = NewStore(fs);
        await fs.WriteAllTextAsync(store.FilePath, "{ not json", Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ListAsync(null, false, Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddAsync("user", "x", Ct));
        Assert.Equal("{ not json", await fs.TryReadAllTextAsync(store.FilePath, Ct));
    }

    [Fact]
    public async Task ShouldRoundTripOnRealDisk_WhenUsingDiskBackedFileSystem()
    {
        var dir = Path.Combine(Path.GetTempPath(), "orkeon-memory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var fs = new DiskBackedFileSystemService(dir, "/workspace");
            var id1 = await NewStore(fs).AddAsync("project", "uses pnpm", Ct);
            var id2 = await NewStore(fs).AddAsync("reference", "docs at /docs", Ct);
            Assert.True(await NewStore(fs).ReplaceAsync(id1, "uses pnpm 9", Ct));
            Assert.True(await NewStore(fs).DeleteAsync(id2, Ct));
            var id3 = await NewStore(fs).AddAsync("user", "short answers", Ct);
            Assert.True(id3 > id2);

            var reread = await NewStore(fs).ListAsync(null, false, Ct);
            Assert.Equal([id1, id3], reread.Select(e => e.Id));
            Assert.Equal("uses pnpm 9", reread[0].Content);

            // The project key is the physical root mounted as /workspace: the folder is its slug, as the session's.
            var slug = ProjectKey.Slug(fs.ResolveAndValidate("/workspace", FileAccessRights.Read).ResolvedPath!);
            Assert.True(File.Exists(Path.Combine(dir, ".orkeon", "memory", slug, "memory.json")));
            var json = await File.ReadAllTextAsync(Path.Combine(dir, ".orkeon", "memory", slug, "memory.json"), Ct);
            Assert.Contains("\"created_at\"", json, StringComparison.Ordinal);
            Assert.Contains("\"next_id\"", json, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
