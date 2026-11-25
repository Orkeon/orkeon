using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Host.Files;
using Orkeon.Host.Tests.Api;
using Orkeon.Host.Tests.Doubles;
using Orkeon.Rag.Abstractions;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Loaders;
using Orkeon.Tools.Analysis.DependencyInjection;

namespace Orkeon.Host.Tests.Files;

/// <summary>
/// What one conversation's files must not go through to reach another: the stores the process shares, and a granted folder
/// that comes to lead where the conversations are kept.
/// </summary>
public sealed class ConversationIsolationTests : IDisposable
{
    private static readonly ConversationRef Alice = new("discord", "1001");
    private static readonly string[] s_inbox = ["/inbox/notes.md"];

    private readonly HostApiHarness _harness = new();
    private readonly List<IHost> _hosts = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Root => Path.Combine(_harness.Root, "conversations");

    public void Dispose()
    {
        foreach (var host in _hosts)
            host.Dispose();
        _harness.Dispose();
    }

    private (IHost Host, ConversationFiles Files) Build(
        Func<IHost, CancellationToken, Task<string>> work, params string[] grants)
    {
        IHost? built = null;
        built = _harness.Build(
            new HeldLlmProvider("unused"),
            port: 1,
            configure: (root, api) =>
            {
                api["Enabled"] = false;
                var host = (Dictionary<string, object?>)((Dictionary<string, object?>)root["Orkeon"]!)["Host"]!;
                host["Files"] = new { Enabled = true, Root };
                host["Crews"] = new object[] { new { Name = "support", Path = _harness.CrewPath, Mounts = grants } };
            },
            services: s =>
            {
                s.Configure<Orkeon.Infrastructure.Configuration.PathSecurityOptions>(o => o.AdditionalAllowedDirectories.Add(_harness.Root));
                s.AddRaggableTree(new RaggableTreeOptions());
                s.AddRaggableTreeTools();
                s.AddScoped<IAgentExecutionService>(_ => new ScriptedAgentExecutionService(ct => work(built!, ct)));
            });
        _hosts.Add(built);
        return (built, built.Services.GetRequiredService<ConversationFiles>());
    }

    private static Task<HostedRunResult> RunAsync(IHost host, bool inConversation = true) =>
        host.Services.GetRequiredService<ICrewRunner>().RunAsync("support", "go", "test", conversation: inConversation
            ? new HostedConversation
            {
                Channel = Alice.Channel,
                ConversationId = Alice.ConversationId,
                PrepareAsync = _ => Task.FromResult(new PreparedRun("go")),
            }
            : null);

    private static async Task<string> CallAsync(IHost host, string tool, Dictionary<string, object?> arguments, CancellationToken ct)
    {
        var found = await host.Services.GetRequiredService<IToolRegistry>().GetToolByNameAsync(tool, ct);
        Assert.NotNull(found);
        var result = await found.CallAsync(new ToolCallRequest(tool, arguments), ct);
        return $"{result.Success}|{result.Error}|{JsonSerializer.Serialize(result.Result)}";
    }

    private static IncomingFile Incoming(string name, string content) =>
        new(name, _ => Task.FromResult(Content(content)));

    private static Stream Content(string text)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return stream;
    }

    private static async Task<string> OutcomeAsync(Func<Task> work)
    {
        try
        {
            await work();
            return "read";
        }
        catch (FileAccessDeniedException ex)
        {
            return ex.Message.Contains("belong to this conversation", StringComparison.Ordinal) ? "private" : ex.Message;
        }
    }

    // ----- stores the process shares -----

    [Theory]
    [InlineData("/inbox")]
    [InlineData("/inbox/")]
    [InlineData("/outbox")]
    [InlineData("//inbox")]
    [InlineData("/")]
    public async Task ShouldIndexNothing_WhenTheRootIsAConversationFolder(string root)
    {
        var answer = string.Empty;
        var (host, files) = Build(async (built, ct) =>
        {
            answer = await CallAsync(built, "index_codebase", new() { ["root_path"] = root }, ct);
            return "done";
        });
        await files.StoreAsync(Alice, null, Incoming("notes.md", "# Alice's secret\n\ntext"), 0, ct: Ct);

        await RunAsync(host);

        Assert.Contains("belong to this conversation", answer, StringComparison.Ordinal);
        Assert.Equal(0, await host.Services.GetRequiredService<IRaggableStore>().GetNodeCountAsync(Ct));
    }

    [Fact]
    public async Task ShouldReindexNothing_WhenAPathIsInAConversationFolder()
    {
        var answer = string.Empty;
        var (host, files) = Build(async (built, ct) =>
        {
            answer = await CallAsync(built, "incremental_reindex", new() { ["root_path"] = "/", ["changed_files"] = s_inbox.ToList() }, ct);
            return "done";
        });
        await files.StoreAsync(Alice, null, Incoming("notes.md", "# Alice's secret\n\ntext"), 0, ct: Ct);

        await RunAsync(host);

        Assert.Contains("belong to this conversation", answer, StringComparison.Ordinal);
        Assert.Equal(0, await host.Services.GetRequiredService<IRaggableStore>().GetNodeCountAsync(Ct));
    }

    [Theory]
    [InlineData("/inbox/notes.md")]
    [InlineData("/inbox/*.md")]
    [InlineData("/**/*.md")]
    [InlineData("/INBOX/**")]
    public async Task ShouldIngestNothing_WhenASourceReachesAConversationFolder(string source)
    {
        var answer = string.Empty;
        var (host, files) = Build(async (built, ct) =>
        {
            answer = await CallAsync(built, "rag_ingest", new() { ["collection"] = "docs", ["sources"] = new List<string> { source } }, ct);
            return "done";
        });
        await files.StoreAsync(Alice, null, Incoming("notes.md", "# Alice's secret\n\ntext"), 0, ct: Ct);

        await RunAsync(host);

        Assert.Contains("belong to this conversation", answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldRefuseAtTheRead_WhenALoaderOrAPatternMeetsAConversationFolder()
    {
        var seen = new List<string>();
        var (host, files) = Build(async (built, ct) =>
        {
            seen.AddRange(await ReadPointsAsync(built, ct));
            return "done";
        });
        await files.StoreAsync(Alice, null, Incoming("notes.md", "text"), 0, ct: Ct);

        await RunAsync(host);

        Assert.Equal(["private", "private"], seen);
    }

    [Fact]
    public async Task ShouldRefuseNothingOfThese_WhenTheRunHasNoConversation()
    {
        var seen = new List<string>();
        var (host, _) = Build(async (built, ct) =>
        {
            await built.Services.GetRequiredService<IFileSystemService>().WriteAllTextAsync("/outbox/notes.md", "text", ct);
            seen.AddRange(await ReadPointsAsync(built, ct, "/outbox"));
            return "done";
        });

        await RunAsync(host, inConversation: false);

        Assert.Equal(["read", "read"], seen);
    }

    private static async Task<string[]> ReadPointsAsync(IHost host, CancellationToken ct, string folder = "/inbox")
    {
        var fs = host.Services.GetRequiredService<IFileSystemService>();
        var loader = new TextFileLoader(fs);
        return
        [
            await OutcomeAsync(async () =>
            {
                await foreach (var document in loader.LoadAsync(new SourceDescriptor { Location = $"{folder}/notes.md" }, ct))
                    Assert.NotNull(document);
            }),
            await OutcomeAsync(() => SourceGlobExpander.ExpandAsync(fs, [$"{folder}/**/*.md"], ct)),
        ];
    }

    // ----- a granted folder that leads to the root -----

    [Fact]
    public async Task ShouldRefuseTheRun_WhenAGrantedFolderCameToLeadToTheConversations()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Symbolic links need a privilege.");
        var ran = 0;
        var folder = Directory.CreateDirectory(Path.Combine(_harness.Root, "ws", "out")).FullName;
        var (host, files) = Build((_, _) =>
        {
            ran++;
            return Task.FromResult("done");
        }, $"{folder}:/data:rw");
        await files.StoreAsync(Alice, null, Incoming("notes.md", "secret"), 0, ct: Ct);
        Directory.Delete(folder);
        Directory.CreateSymbolicLink(folder, Root);

        var result = await RunAsync(host);
        var crew = new HostedCrewOptions { Name = "support", Path = _harness.CrewPath, Mounts = [$"{folder}:/data:rw"] };
        var refused = await Assert.ThrowsAsync<ConversationPreparationException>(
            () => files.UseAsync(Alice, crew, (_, _) => Task.FromResult(true), Ct));

        Assert.Equal(HostedRunOutcome.Failed, result.Outcome);
        Assert.Equal(0, ran);
        Assert.DoesNotContain(_harness.Root, result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_harness.Root, refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("conversations")]
    [InlineData(".")]
    public async Task ShouldReadNothingThroughAGrantedFolder_WhenItIsSwappedDuringTheRun(string target)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Symbolic links need a privilege.");
        var seen = new List<string>();
        var parent = Directory.CreateDirectory(Path.Combine(_harness.Root, "ws")).FullName;
        var folder = Directory.CreateDirectory(Path.Combine(parent, "out")).FullName;
        var (host, files) = Build(async (built, ct) =>
        {
            var fs = built.Services.GetRequiredService<IFileSystemService>();
            await fs.WriteAllTextAsync("/data/a.txt", "a", ct);
            Directory.Delete(folder, recursive: true);
            Directory.CreateSymbolicLink(folder, Path.GetFullPath(Path.Combine(_harness.Root, target)));
            await foreach (var entry in fs.EnumerateFilesAsync("/data", new VirtualEnumerationOptions(), ct))
                seen.Add(entry.VirtualPath);
            return "done";
        }, $"{parent}:/ws:rw", $"{folder}:/data:rw");
        await files.StoreAsync(Alice, null, Incoming("notes.md", "secret"), 0, ct: Ct);

        await RunAsync(host);

        Assert.Empty(seen);
        Directory.Delete(folder);
    }

    // ----- the code sandbox, one root for every run -----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShouldHideAnotherRunsSandboxFromAnAgent_WhenItAsksByEitherSpace(bool physical)
    {
        const string content = "code of another conversation";
        var read = string.Empty;
        var asked = string.Empty;
        var (host, _) = Build(async (built, ct) =>
        {
            read = await CallAsync(built, "file_read", new() { ["path"] = asked }, ct);
            return "done";
        });
        var sandbox = host.Services.GetRequiredService<FileSystemRegistry>().GetMounts().Single(m => m.VirtualPath == "/sandbox");
        var folder = Directory.CreateDirectory(Path.Combine(sandbox.BasePath, $"run-{Guid.NewGuid():N}"));
        var file = Path.Combine(folder.FullName, "Program.cs");
        await File.WriteAllTextAsync(file, content, Ct);
        asked = physical ? file : $"/sandbox/{folder.Name}/Program.cs";
        try
        {
            await RunAsync(host);
        }
        finally
        {
            folder.Delete(recursive: true);
        }

        Assert.StartsWith("False|", read, StringComparison.Ordinal);
        Assert.DoesNotContain(content, read, StringComparison.Ordinal);
    }
}
