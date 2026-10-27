using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Domain.Tools.Security;
using Orkeon.Infrastructure.Session;
using Orkeon.Infrastructure.Tools;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Session;

/// <summary>RT-14: the REPL session is persisted per project and rehydrated by a new service.</summary>
public sealed class FileSessionBufferServiceTests
{
    private const string Sessions = "/workspace/.orkeon/sessions";
    private const string ProjectA = "/workspace/project-a";
    private const string ProjectB = "/workspace/project-b";

    private static FakeFileSystemService NewFs() =>
        new FakeFileSystemService().AddMount("/workspace", FileAccessRights.ReadWrite);

    private static FileSessionBufferService NewService(FakeFileSystemService fs) =>
        new(fs, new CliSessionOptions { SessionsDirectory = Sessions });

    private static async Task<List<VirtualFileEntry>> FilesAsync(FakeFileSystemService fs)
    {
        var files = new List<VirtualFileEntry>();
        await foreach (var f in fs.EnumerateFilesAsync(Sessions,
                           new VirtualEnumerationOptions { Recursive = true }, TestContext.Current.CancellationToken))
            if (f.Kind == VirtualEntryKind.File) files.Add(f);
        return files;
    }

    private static async Task<FileSessionBufferService> TurnAsync(FakeFileSystemService fs, string project)
    {
        var svc = NewService(fs);
        await svc.LoadAsync(project, TestContext.Current.CancellationToken);
        svc.ReplaceMessages([
            new SessionMessage { Role = "user", Content = "question-42" },
            new SessionMessage { Role = "assistant", Content = "answer-42" },
        ]);
        svc.SetState("breaker", "3");
        svc.SetTitle("My title");
        await svc.SaveAsync(TestContext.Current.CancellationToken);
        return svc;
    }

    [Fact]
    [Trait("BR", "RT-14")]
    public async Task ShouldPersistBufferAndState_WhenTurnEnds()
    {
        var fs = NewFs();
        var svc = await TurnAsync(fs, ProjectA);

        var file = Assert.Single(await FilesAsync(fs));
        Assert.EndsWith("/last.json", file.VirtualPath);
        var json = await fs.TryReadAllTextAsync(file.VirtualPath, TestContext.Current.CancellationToken);
        Assert.NotNull(json);
        Assert.Contains("question-42", json);
        Assert.Contains("answer-42", json);
        Assert.Contains("breaker", json);
        Assert.Contains("My title", json);
        Assert.Contains(svc.GetMetadata().SessionId, json);
    }

    [Fact]
    [Trait("BR", "RT-14")]
    public async Task ShouldReloadLastSession_WhenServiceStartsOnSameProject()
    {
        var fs = NewFs();
        var first = await TurnAsync(fs, ProjectA);

        var second = NewService(fs);
        await second.LoadAsync(ProjectA, TestContext.Current.CancellationToken);

        var messages = second.GetMessages();
        Assert.Equal(2, messages.Count);
        Assert.Equal("user", messages[0].Role);
        Assert.Equal("question-42", messages[0].Content);
        Assert.Equal("answer-42", messages[1].Content);
        Assert.Equal("3", second.GetState("breaker"));
        Assert.Equal("My title", second.GetMetadata().Title);
        Assert.Equal(first.GetMetadata().SessionId, second.GetMetadata().SessionId);
    }

    [Fact]
    [Trait("BR", "RT-14")]
    public async Task ShouldReadReloadedBuffer_WhenSessionStoreToolCalledAfterRestart()
    {
        var fs = NewFs();
        await TurnAsync(fs, ProjectA);
        var reloaded = NewService(fs);
        await reloaded.LoadAsync(ProjectA, TestContext.Current.CancellationToken);
        using var tool = new SessionStoreTool(reloaded);

        var resp = await tool.CallAsync(new ToolCallRequest("session_store", new Dictionary<string, object?>
        {
            ["operation"] = "read_messages",
        }), TestContext.Current.CancellationToken);

        Assert.True(resp.Success, resp.Error);
        var dict = Assert.IsType<Dictionary<string, object?>>(resp.Result);
        var messages = Assert.IsType<System.Collections.IEnumerable>(dict["messages"], exactMatch: false)
            .Cast<object>().ToList();
        Assert.Equal(2, messages.Count);
        var contents = messages
            .Select(m => Assert.IsType<Dictionary<string, object?>>(m)["content"]?.ToString())
            .ToList();
        Assert.Equal(["question-42", "answer-42"], contents);
    }

    [Fact]
    [Trait("BR", "RT-14")]
    public async Task ShouldStartEmpty_WhenSessionFileIsCorrupt()
    {
        var fs = NewFs();
        await TurnAsync(fs, ProjectA);
        var file = Assert.Single(await FilesAsync(fs));
        fs.AddFile(file.VirtualPath, "{ not json ]]");

        var svc = NewService(fs);
        await svc.LoadAsync(ProjectA, TestContext.Current.CancellationToken);

        Assert.Equal(0, svc.MessageCount);
        Assert.Null(svc.GetState("breaker"));
        Assert.Null(svc.GetMetadata().Title);
    }

    [Fact]
    [Trait("BR", "RT-14")]
    public async Task ShouldNotReloadOtherProjectSession_WhenProjectDiffers()
    {
        var fs = NewFs();
        var a = await TurnAsync(fs, ProjectA);

        var b = NewService(fs);
        await b.LoadAsync(ProjectB, TestContext.Current.CancellationToken);

        Assert.Equal(0, b.MessageCount);
        Assert.Null(b.GetState("breaker"));
        Assert.NotEqual(a.GetMetadata().SessionId, b.GetMetadata().SessionId);
    }

    [Fact]
    [Trait("BR", "#227")]
    public async Task ShouldNameTheProjectFile_OnceTheProjectIsLoaded()
    {
        var fs = NewFs();
        var svc = NewService(fs);
        Assert.Null(svc.SessionFilePath);

        await svc.LoadAsync(ProjectA, TestContext.Current.CancellationToken);
        svc.ReplaceMessages([new SessionMessage { Role = "user", Content = "now" }]);
        await svc.SaveAsync(TestContext.Current.CancellationToken);

        var file = Assert.Single(await FilesAsync(fs));
        Assert.Equal(file.VirtualPath, svc.SessionFilePath);
    }

    [Fact]
    [Trait("BR", "#227")]
    public async Task ShouldSwapBufferStateAndTitle_WhenLoadingASavedFile()
    {
        var fs = NewFs();
        var saved = await TurnAsync(fs, ProjectA);
        var path = saved.SessionFilePath!;
        var running = NewService(fs);
        await running.LoadAsync(ProjectB, TestContext.Current.CancellationToken);
        running.ReplaceMessages([new SessionMessage { Role = "user", Content = "other" }]);
        running.SetState("other", "1");

        await running.LoadFromAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(["question-42", "answer-42"], running.GetMessages().Select(m => m.Content));
        Assert.Equal("3", running.GetState("breaker"));
        Assert.Null(running.GetState("other"));
        Assert.Equal("My title", running.GetMetadata().Title);
        Assert.Equal(saved.GetMetadata().SessionId, running.GetMetadata().SessionId);
    }

    [Fact]
    [Trait("BR", "#227")]
    public async Task ShouldKeepSavingToItsOwnFile_AfterLoadingAnother()
    {
        var fs = NewFs();
        var saved = await TurnAsync(fs, ProjectA);
        var running = NewService(fs);
        await running.LoadAsync(ProjectB, TestContext.Current.CancellationToken);
        var own = running.SessionFilePath;

        await running.LoadFromAsync(saved.SessionFilePath!, TestContext.Current.CancellationToken);
        running.AppendNote("after-load");
        await running.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(own, running.SessionFilePath);
        var json = await fs.TryReadAllTextAsync(own!, TestContext.Current.CancellationToken);
        Assert.Contains("after-load", json);
        var other = await fs.TryReadAllTextAsync(saved.SessionFilePath!, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("after-load", other);
    }

    [Theory]
    [Trait("BR", "#227")]
    [InlineData("{ not json ]]")]
    [InlineData("[]")]
    [InlineData("{\"Title\":\"t\",\"Messages\":[],\"State\":{}}")]
    [InlineData("{\"SessionId\":\"s\",\"State\":{}}")]
    [InlineData("{\"SessionId\":\"s\",\"Messages\":[]}")]
    [InlineData("{\"SessionId\":\"s\",\"Messages\":[{\"Role\":\"user\"}],\"State\":{}}")]
    [InlineData("{\"SessionId\":\"s\",\"Messages\":[null],\"State\":{}}")]
    [InlineData("{\"SessionId\":\"s\",\"Messages\":[{\"Role\":null,\"Content\":\"c\"}],\"State\":{}}")]
    public async Task ShouldRefuseAndLeaveTheSessionAsItWas_WhenTheFileIsNotOneTheHostSaved(string content)
    {
        var fs = NewFs();
        var running = await TurnAsync(fs, ProjectA);
        var before = running.GetMetadata().SessionId;
        fs.AddFile("/workspace/foreign.json", content);

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => running.LoadFromAsync("/workspace/foreign.json", TestContext.Current.CancellationToken));

        Assert.Contains("/workspace/foreign.json", error.Message, StringComparison.Ordinal);
        Assert.Equal(["question-42", "answer-42"], running.GetMessages().Select(m => m.Content));
        Assert.Equal("3", running.GetState("breaker"));
        Assert.Equal("My title", running.GetMetadata().Title);
        Assert.Equal(before, running.GetMetadata().SessionId);
    }

    [Fact]
    [Trait("BR", "#227")]
    public async Task ShouldRefuse_WhenTheFileDoesNotExist()
    {
        var fs = NewFs();
        var running = await TurnAsync(fs, ProjectA);

        var error = await Assert.ThrowsAsync<FileNotFoundException>(
            () => running.LoadFromAsync("/workspace/missing.json", TestContext.Current.CancellationToken));

        Assert.Contains("/workspace/missing.json", error.Message, StringComparison.Ordinal);
        Assert.Equal(2, running.MessageCount);
    }
}
