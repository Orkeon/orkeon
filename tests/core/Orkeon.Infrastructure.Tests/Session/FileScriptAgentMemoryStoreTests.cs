using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Infrastructure.Session;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Session;

/// <summary>RT-16: ctx.memory.agent is backed by one JSON file per project and agent.</summary>
public sealed class FileScriptAgentMemoryStoreTests
{
    private const string Root = "/workspace/.orkeon/agent-memory";
    private const string Sessions = "/workspace/.orkeon/sessions";
    private const string ProjectA = "/workspace/project-a";
    private const string ProjectB = "/workspace/project-b";
    private const string SlugA = "project-a-3b27f97a4179";
    private static string AgentKey(string project, string id) =>
        new ScriptAgentMemoryKey { Project = project, Crew = "crew", Agent = id }.ToString();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FakeFileSystemService NewFs() =>
        new FakeFileSystemService().AddMount("/workspace", FileAccessRights.ReadWrite);

    private static FileScriptAgentMemoryStore NewStore(IFileSystemService fs) => new(fs, Root);

    private static async Task<List<VirtualFileEntry>> FilesAsync(IFileSystemService fs, string root)
    {
        var files = new List<VirtualFileEntry>();
        await foreach (var f in fs.EnumerateFilesAsync(root, new VirtualEnumerationOptions { Recursive = true }, Ct))
            if (f.Kind == VirtualEntryKind.File) files.Add(f);
        return files;
    }

    [Fact]
    [Trait("BR", "RT-16")]
    [Trait("BR", "R5")]
    public async Task ShouldPersistThroughFileStore_WhenHostProvidesPort()
    {
        var fs = NewFs();
        var a1 = AgentKey(ProjectA, "a1");
        await NewStore(fs).StoreAsync(a1, "alpha-key", "{\"n\":1}", Ct);
        await NewStore(fs).StoreAsync(a1, "other", "{\"n\":2}", Ct);
        await NewStore(fs).StoreAsync(AgentKey(ProjectA, "a2"), "alpha-key", "\"for-a2\"", Ct);
        await NewStore(fs).StoreAsync(AgentKey(ProjectB, "a1"), "alpha-key", "\"for-b\"", Ct);

        var next = NewStore(fs);
        Assert.Equal("{\"n\":1}", await next.GetAsync(a1, "alpha-key", Ct));
        Assert.Equal("\"for-a2\"", await next.GetAsync(AgentKey(ProjectA, "a2"), "alpha-key", Ct));
        Assert.Equal("\"for-b\"", await next.GetAsync(AgentKey(ProjectB, "a1"), "alpha-key", Ct));
        Assert.Null(await next.GetAsync(a1, "absent", Ct));

        var hit = Assert.Single(await next.SearchAsync(a1, "alpha", 5, Ct));
        Assert.Equal("alpha-key", hit.Key);
        Assert.Equal("{\"n\":1}", hit.ValueJson);

        var path = $"{Root}/{SlugA}/crew/a1.json";
        var json = await fs.TryReadAllTextAsync(path, Ct);
        Assert.NotNull(json);
        Assert.DoesNotContain("\\\"n\\\"", json, StringComparison.Ordinal);

        Assert.True(await next.DeleteAsync(a1, "alpha-key", Ct));
        Assert.False(await next.DeleteAsync(a1, "alpha-key", Ct));
        Assert.Null(await NewStore(fs).GetAsync(a1, "alpha-key", Ct));
        Assert.Equal("{\"n\":2}", await NewStore(fs).GetAsync(a1, "other", Ct));
    }

    [Fact]
    [Trait("BR", "RT-16")]
    [Trait("BR", "R5")]
    public async Task ShouldRefuseKey_WhenItHoldsNewline()
    {
        var fs = NewFs();
        var store = NewStore(fs);

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => store.StoreAsync(AgentKey(ProjectA, "a1"), "bad\nkey", "1", Ct));

        Assert.Empty(await FilesAsync(fs, Root));
    }

    [Fact]
    [Trait("BR", "RT-16")]
    [Trait("BR", "R5")]
    public async Task ShouldRestoreBackup_WhenWriteFails()
    {
        var failing = new FailingWriteFileSystemService(NewFs());
        var a1 = AgentKey(ProjectA, "a1");
        await NewStore(failing).StoreAsync(a1, "k1", "{\"n\":1}", Ct);

        failing.FailNextJsonWrites = 1;
        await Assert.ThrowsAnyAsync<IOException>(() => NewStore(failing).StoreAsync(a1, "k2", "{\"n\":2}", Ct));

        var reread = NewStore(failing);
        Assert.Equal("{\"n\":1}", await reread.GetAsync(a1, "k1", Ct));
        Assert.Null(await reread.GetAsync(a1, "k2", Ct));
    }

    [Fact]
    [Trait("BR", "RT-16")]
    [Trait("BR", "R5")]
    public async Task ShouldKeepEveryValue_WhenStoresRunConcurrentlyOnOneAgent()
    {
        var fs = NewFs();
        var store = NewStore(fs);
        var a1 = AgentKey(ProjectA, "a1");

        await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(i => Task.Run(() => store.StoreAsync(a1, $"k{i}", i.ToString(), Ct), Ct)));

        var reread = NewStore(fs);
        for (var i = 0; i < 20; i++)
            Assert.Equal(i.ToString(), await reread.GetAsync(a1, $"k{i}", Ct));
    }

    [Fact]
    [Trait("BR", "RT-16")]
    [Trait("BR", "R5")]
    public async Task ShouldRefuseUnreadableFile_InsteadOfOverwriting_WhenMemoryFileIsCorrupt()
    {
        const string corrupt = "{\"k\":";
        const string backup = "{\"old\":1}";
        var path = $"{Root}/{SlugA}/crew/a1.json";
        var fs = NewFs().AddFile(path, corrupt).AddFile(path + ".bak", backup);
        var a1 = AgentKey(ProjectA, "a1");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => NewStore(fs).GetAsync(a1, "k", Ct));
        Assert.Contains("a1.json", ex.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidOperationException>(() => NewStore(fs).SearchAsync(a1, "k", 5, Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => NewStore(fs).StoreAsync(a1, "k2", "1", Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => NewStore(fs).DeleteAsync(a1, "k", Ct));

        Assert.Equal(corrupt, await fs.TryReadAllTextAsync(path, Ct));
        Assert.Equal(backup, await fs.TryReadAllTextAsync(path + ".bak", Ct));
    }

    [Fact]
    [Trait("BR", "RT-16")]
    [Trait("BR", "R5")]
    public async Task ShouldRoundTripOnRealDisk_WhenUsingDiskBackedFileSystem()
    {
        var dir = Path.Combine(Path.GetTempPath(), "orkeon-agent-memory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var fs = new DiskBackedFileSystemService(dir, "/workspace");
            var a1 = AgentKey(ProjectA, "a1");
            var a2 = AgentKey(ProjectA, "a2");
            await NewStore(fs).StoreAsync(a1, "k1", "{\"n\":1}", Ct);
            await NewStore(fs).StoreAsync(a1, "k2", "\"two\"", Ct);
            await NewStore(fs).StoreAsync(a2, "k1", "{\"n\":3}", Ct);

            var next = NewStore(fs);
            Assert.Equal("{\"n\":1}", await next.GetAsync(a1, "k1", Ct));
            Assert.Equal("\"two\"", await next.GetAsync(a1, "k2", Ct));
            Assert.Equal("{\"n\":3}", await next.GetAsync(a2, "k1", Ct));

            Assert.True(await next.DeleteAsync(a1, "k1", Ct));
            var reread = NewStore(fs);
            Assert.Null(await reread.GetAsync(a1, "k1", Ct));
            Assert.Equal("\"two\"", await reread.GetAsync(a1, "k2", Ct));
            Assert.Equal("{\"n\":3}", await reread.GetAsync(a2, "k1", Ct));
            Assert.True(File.Exists(Path.Combine(dir, ".orkeon", "agent-memory", SlugA, "crew", "a1.json")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    [Trait("BR", "RT-16")]
    [Trait("BR", "R5")]
    public async Task ShouldDeriveSameProjectKey_ForSessionBufferAndAgentMemory()
    {
        Assert.Equal(SlugA, ProjectKey.Slug(ProjectA));

        var fs = NewFs();
        var session = new FileSessionBufferService(fs, new CliSessionOptions { SessionsDirectory = Sessions });
        await session.LoadAsync(ProjectA, Ct);
        session.SetTitle("t");
        await session.SaveAsync(Ct);
        var sessionFile = Assert.Single(await FilesAsync(fs, Sessions));

        await NewStore(fs).StoreAsync(AgentKey(ProjectA, "a1"), "k", "1", Ct);
        var memoryFile = Assert.Single(await FilesAsync(fs, Root));

        Assert.Equal($"{Sessions}/{SlugA}/last.json", sessionFile.VirtualPath);
        Assert.Equal($"{Root}/{SlugA}/crew/a1.json", memoryFile.VirtualPath);
    }

    [Fact]
    [Trait("BR", "R5")]
    public async Task ShouldKeepSeparateFiles_WhenTwoCrewsShareAnAgentName()
    {
        var fs = NewFs();
        var crewA = new ScriptAgentMemoryKey { Project = ProjectA, Crew = "crewA", Agent = "agent1" }.ToString();
        var crewB = new ScriptAgentMemoryKey { Project = ProjectA, Crew = "crewB", Agent = "agent1" }.ToString();

        await NewStore(fs).StoreAsync(crewA, "k", "\"from-a\"", Ct);
        await NewStore(fs).StoreAsync(crewB, "k", "\"from-b\"", Ct);

        var next = NewStore(fs);
        Assert.Equal("\"from-a\"", await next.GetAsync(crewA, "k", Ct));
        Assert.Equal("\"from-b\"", await next.GetAsync(crewB, "k", Ct));
        var files = await FilesAsync(fs, Root);
        Assert.Equal(2, files.Select(f => f.VirtualPath).Distinct().Count());
    }

    [Fact]
    [Trait("BR", "R5")]
    public async Task ShouldWriteUnderItsOwnFolder_WhenCrewHasNoName()
    {
        var fs = NewFs();
        var noCrew = new ScriptAgentMemoryKey { Project = ProjectA, Crew = null, Agent = "a1" }.ToString();
        var named = new ScriptAgentMemoryKey { Project = ProjectA, Crew = "crew", Agent = "a1" }.ToString();

        await NewStore(fs).StoreAsync(noCrew, "k", "\"none\"", Ct);
        await NewStore(fs).StoreAsync(named, "k", "\"named\"", Ct);

        var next = NewStore(fs);
        Assert.Equal("\"none\"", await next.GetAsync(noCrew, "k", Ct));
        Assert.Equal("\"named\"", await next.GetAsync(named, "k", Ct));
        var files = await FilesAsync(fs, Root);
        Assert.Equal(2, files.Select(f => f.VirtualPath).Distinct().Count());
        Assert.Contains(files, f => f.VirtualPath == $"{Root}/{SlugA}/crew/a1.json");
    }

    [Fact]
    [Trait("BR", "R5")]
    public async Task ShouldKeepCrewNamedNoneApart_WhenCrewHasNoName()
    {
        var fs = NewFs();
        var noCrew = new ScriptAgentMemoryKey { Project = ProjectA, Crew = null, Agent = "a1" }.ToString();
        var literal = new ScriptAgentMemoryKey { Project = ProjectA, Crew = "%none", Agent = "a1" }.ToString();

        await NewStore(fs).StoreAsync(noCrew, "k", "\"no-crew\"", Ct);
        await NewStore(fs).StoreAsync(literal, "k", "\"literal\"", Ct);

        var next = NewStore(fs);
        Assert.Equal("\"no-crew\"", await next.GetAsync(noCrew, "k", Ct));
        Assert.Equal("\"literal\"", await next.GetAsync(literal, "k", Ct));
        var files = await FilesAsync(fs, Root);
        Assert.Equal(2, files.Select(f => f.VirtualPath).Distinct().Count());
    }
}
