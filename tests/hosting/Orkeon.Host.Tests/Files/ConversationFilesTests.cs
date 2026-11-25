using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Services.Checkpointing;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Domain.FileSystem;
using Orkeon.Host.Api;
using Orkeon.Host.Files;
using Orkeon.Host.Tests.Api;
using Orkeon.Host.Tests.Doubles;
using Orkeon.Infrastructure.Checkpointing;

namespace Orkeon.Host.Tests.Files;

/// <summary>
/// The folders of the chat conversations, against the composition the daemon runs: the real VFS, the real runner, the
/// agents' work scripted by the test.
/// </summary>
public sealed class ConversationFilesTests : IDisposable
{
    private static readonly ConversationRef Alice = new("discord", "1001");
    private static readonly ConversationRef Bob = new("discord", "1002");

    private readonly HostApiHarness _harness = new();
    private readonly List<IHost> _hosts = [];

    private static readonly string[] s_extensions = [".TXT", "pdf"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Root => Path.Combine(_harness.Root, "conversations");

    public void Dispose()
    {
        foreach (var host in _hosts)
            host.Dispose();
        _harness.Dispose();
    }

    private (IHost Host, ConversationFiles Files) Build(
        Func<IFileSystemService, CancellationToken, Task<string>>? work = null,
        Action<Dictionary<string, object?>>? files = null,
        bool enabled = true,
        Action<Dictionary<string, object?>>? orkeon = null,
        Action<IServiceCollection>? services = null)
    {
        IHost? built = null;
        built = _harness.Build(
            new HeldLlmProvider("unused"),
            port: 1,
            configure: (root, api) =>
            {
                api["Enabled"] = false;
                var section = new Dictionary<string, object?> { ["Enabled"] = enabled, ["Root"] = Root };
                files?.Invoke(section);
                var all = (Dictionary<string, object?>)root["Orkeon"]!;
                ((Dictionary<string, object?>)all["Host"]!)["Files"] = section;
                orkeon?.Invoke(all);
            },
            services: s =>
            {
                if (work is not null)
                {
                    s.AddScoped<IAgentExecutionService>(_ => new ScriptedAgentExecutionService(
                        ct => work(built!.Services.GetRequiredService<IFileSystemService>(), ct)));
                }

                services?.Invoke(s);
            });
        _hosts.Add(built);
        return (built, built.Services.GetService<ConversationFiles>()!);
    }

    private static HostedConversation In(ConversationRef conversation, Func<CancellationToken, Task<PreparedRun>>? prepare = null) => new()
    {
        Channel = conversation.Channel,
        ConversationId = conversation.ConversationId,
        PrepareAsync = prepare ?? (_ => Task.FromResult(new PreparedRun("go"))),
    };

    private static Task<HostedRunResult> RunAsync(IHost host, HostedConversation? conversation) =>
        host.Services.GetRequiredService<ICrewRunner>().RunAsync("support", "go", "test", conversation: conversation);

    private static IncomingFile Incoming(string name, string content, string? id = null, long? announced = null) =>
        new(name, _ => Task.FromResult(Content(content))) { Id = id, Size = announced };

    private static Stream Content(string text)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return stream;
    }

    private string Inbox(ConversationRef conversation) => Path.Combine(Root, conversation.Key, "in");

    private string Outbox(ConversationRef conversation) => Path.Combine(Root, conversation.Key, "out");

    private string Meta(ConversationRef conversation) => Path.Combine(Root, conversation.Key, "meta.json");

    private void Age(ConversationRef conversation, TimeSpan age)
    {
        var meta = JsonNode.Parse(Read(Meta(conversation)))!;
        meta["LastActivity"] = DateTimeOffset.UtcNow - age;
        Write(Meta(conversation), meta.ToJsonString());
    }

    private static string Read(string path) => File.ReadAllText(path);

    private static void Write(string path, string content) => File.WriteAllText(path, content);

    private static async Task<string> ReadAsync(IFileSystemService fs, string path, CancellationToken ct) =>
        await fs.TryReadAllTextAsync(path, ct) ?? throw new FileNotFoundException(path);

    private static async Task<bool> DeniedAsync(Func<Task> work)
    {
        try
        {
            await work();
            return false;
        }
        catch (Exception ex) when (ex is FileAccessDeniedException or IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    // ----- two conversations -----

    [Fact]
    public async Task ShouldKeepTwoConversationsApart_WhenTheyRunOneCrewAtOnce()
    {
        using var both = new Barrier(2);
        var (host, files) = Build(async (fs, ct) =>
        {
            var mine = await ReadAsync(fs, "/inbox/secret.txt", ct);
            Assert.True(both.SignalAndWait(TimeSpan.FromSeconds(60), ct));
            await fs.WriteAllTextAsync("/outbox/answer.txt", mine, ct);
            Assert.True(both.SignalAndWait(TimeSpan.FromSeconds(60), ct));
            var seen = new List<string>();
            foreach (var folder in (string[])["/inbox", "/outbox"])
            {
                await foreach (var entry in fs.EnumerateFilesAsync(folder, new VirtualEnumerationOptions(), ct))
                    seen.Add(entry.VirtualPath);
            }

            return $"{await ReadAsync(fs, "/outbox/answer.txt", ct)}:{string.Join(',', seen)}";
        });
        await files.StoreAsync(Alice, null, Incoming("secret.txt", "alice"), 0, ct: Ct);
        await files.StoreAsync(Bob, null, Incoming("secret.txt", "bob"), 0, ct: Ct);

        var results = await Task.WhenAll(
            Task.Run(() => RunAsync(host, In(Alice)), Ct), Task.Run(() => RunAsync(host, In(Bob)), Ct));

        Assert.Equal("alice:/inbox/secret.txt,/outbox/answer.txt", results[0].Message);
        Assert.Equal("bob:/inbox/secret.txt,/outbox/answer.txt", results[1].Message);
        Assert.Equal("alice", Read(Path.Combine(Outbox(Alice), "answer.txt")));
        Assert.Equal("bob", Read(Path.Combine(Outbox(Bob), "answer.txt")));
    }

    [Fact]
    public async Task ShouldGiveTheRunAReadOnlyInboxAndNothingElseOfItsConversation_WhenItRuns()
    {
        var denied = new List<bool>();
        var (host, files) = Build(async (fs, ct) =>
        {
            denied.Add(await DeniedAsync(() => fs.WriteAllTextAsync("/inbox/new.txt", "x", ct)));
            denied.Add(await DeniedAsync(() => fs.DeleteAsync("/inbox/a.txt", recursive: false, ct)));
            denied.Add(await DeniedAsync(() => ReadAsync(fs, "/inbox/.conversation/meta.json", ct)));
            denied.Add(await DeniedAsync(() => ReadAsync(fs, "/inbox/../meta.json", ct)));
            denied.Add(await DeniedAsync(() => ReadAsync(fs, "/outbox/../meta.json", ct)));
            return "done";
        });
        await files.StoreAsync(Alice, null, Incoming("a.txt", "a"), 0, ct: Ct);

        Assert.Equal("done", (await RunAsync(host, In(Alice))).Message);

        Assert.Equal([true, true, true, true, true], denied);
        Assert.Equal("a", Read(Path.Combine(Inbox(Alice), "a.txt")));
    }

    [Fact]
    public async Task ShouldNotFollowALinkToAnotherConversation_WhenARunFindsOneInItsOutbox()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Symbolic links need a privilege.");
        var denied = new List<bool>();
        var (host, files) = Build(async (fs, ct) =>
        {
            denied.Add(await DeniedAsync(() => ReadAsync(fs, "/outbox/file.txt", ct)));
            denied.Add(await DeniedAsync(() => ReadAsync(fs, "/outbox/folder/b.txt", ct)));
            denied.Add(await DeniedAsync(() => fs.WriteAllTextAsync("/outbox/folder/new.txt", "x", ct)));
            return "done";
        });
        await files.StoreAsync(Bob, null, Incoming("b.txt", "bob"), 0, ct: Ct);
        await files.StoreAsync(Alice, null, Incoming("a.txt", "a"), 0, ct: Ct);
        File.CreateSymbolicLink(Path.Combine(Outbox(Alice), "file.txt"), Path.Combine(Inbox(Bob), "b.txt"));
        Directory.CreateSymbolicLink(Path.Combine(Outbox(Alice), "folder"), Inbox(Bob));

        await RunAsync(host, In(Alice));

        Assert.Equal([true, true, true], denied);
        Assert.Single(Directory.GetFiles(Inbox(Bob)));
    }

    [Fact]
    public async Task ShouldNeverMixTheHostsNamespaceAndTheRuns_WhenBothAreUsedAtOnce()
    {
        var inRun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hostDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refused = false;
        ConversationFiles? files = null;
        var (host, built) = Build(async (fs, ct) =>
        {
            inRun.SetResult();
            await hostDone.Task.WaitAsync(TimeSpan.FromSeconds(60), ct);
            refused = await DeniedAsync(() => fs.WriteAllTextAsync("/inbox/agent.txt", "x", ct));
            return await ReadAsync(fs, "/inbox/later.txt", ct);
        });
        files = built;

        var run = RunAsync(host, In(Alice, async ct =>
        {
            // The preparation writes in the inbox, by the host's mount set, and leaves it before the run's is entered.
            await files.StoreAsync(Alice, null, Incoming("first.txt", "1"), 0, ct: ct);
            return new PreparedRun("go");
        }));
        await inRun.Task.WaitAsync(TimeSpan.FromSeconds(60), Ct);
        // While the run is inside its own, the host enters its own for the same conversation, then leaves.
        var stored = await files.StoreAsync(Alice, null, Incoming("later.txt", "2"), 0, ct: Ct);
        var outside = await DeniedAsync(() => ReadAsync(host.Services.GetRequiredService<IFileSystemService>(), "/inbox/later.txt", Ct));
        hostDone.SetResult();

        Assert.Equal("2", (await run).Message);
        Assert.True(refused);
        Assert.True(outside);
        Assert.Equal("/inbox/later.txt", stored.Path);
        Assert.Null(host.Services.GetRequiredService<IFileSystemScope>().Current);
    }

    [Fact]
    public async Task ShouldHideTheInbox_WhenAnInternalMountCoversTheRoot()
    {
        // Why no mount is made of the root: an Internal one hides what is under it, the conversations' own mounts with it.
        var (host, files) = Build();
        await files.StoreAsync(Alice, null, Incoming("a.txt", "a"), 0, ct: Ct);
        using var registry = new FileSystemRegistry(
        [
            new FileSystemMount(Root, "/conversations", FileAccessRights.ReadWrite, null, MountVisibility.Internal),
            new FileSystemMount(Inbox(Alice), "/inbox", FileAccessRights.Read),
        ]);
        using var entered = host.Services.GetRequiredService<IFileSystemScope>().Enter(registry);

        Assert.True(await DeniedAsync(() => ReadAsync(host.Services.GetRequiredService<IFileSystemService>(), "/inbox/a.txt", Ct)));
    }

    [Fact]
    public async Task ShouldGiveEachRunTwoEmptyFoldersAndDeleteThem_WhenItHasNoConversation()
    {
        var during = -1;
        var (host, _) = Build(async (fs, ct) =>
        {
            during = Directory.GetDirectories(Root).Length;
            await fs.WriteAllTextAsync("/outbox/a.txt", "x", ct);
            var count = 0;
            await foreach (var _ in fs.EnumerateFilesAsync("/inbox", new VirtualEnumerationOptions(), ct))
                count++;
            return $"{count}";
        });

        var result = await RunAsync(host, conversation: null);

        Assert.Equal("0", result.Message);
        Assert.Equal("/outbox/a.txt", Assert.Single(result.OutboxFiles).Path);
        Assert.Equal(1, during);
        Assert.Empty(Directory.GetDirectories(Root));
    }

    [Fact]
    public async Task ShouldNameTheFolderByTheKey_WhenTheIdIsATeamsOne()
    {
        var teams = new ConversationRef("teams", "19:abc/def..\\..@thread.tacv2;messageid=1758900000000");
        var (host, files) = Build((_, _) => Task.FromResult("done"));

        await files.StoreAsync(teams, null, Incoming("../../evil\\name?.txt", "x"), 0, ct: Ct);
        await RunAsync(host, In(teams));

        Assert.Equal(teams.Key, Path.GetFileName(Assert.Single(Directory.GetDirectories(Root))));
        Assert.Matches("^[0-9a-f]{64}$", teams.Key);
        Assert.Single(Directory.GetFiles(Inbox(teams)));
        Assert.Contains(teams.ConversationId, JsonNode.Parse(Read(Meta(teams)))!["ConversationId"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldOpenTheFoldersToTheServiceAlone_WhenItMakesThem()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "POSIX permissions.");
        var (_, files) = Build();

        await files.StoreAsync(Alice, null, Incoming("a.txt", "a"), 0, ct: Ct);

        if (OperatingSystem.IsWindows())
            return; // Skipped above: the analyzer does not know it.
        foreach (var folder in (string[])[Root, Path.Combine(Root, Alice.Key), Inbox(Alice), Outbox(Alice)])
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(folder));
    }

    // ----- the outbox list -----

    [Fact]
    public async Task ShouldListNewAndChangedFilesOnly_WhenTheRunEnds()
    {
        var (host, files) = Build(async (fs, ct) =>
        {
            await fs.WriteAllTextAsync("/outbox/new.txt", "12345", ct);
            await fs.WriteAllTextAsync("/outbox/sub/deep.txt", "1", ct);
            await fs.WriteAllTextAsync("/outbox/changed.txt", "longer than before", ct);
            Directory.CreateDirectory(Path.Combine(Outbox(Alice), ".cache"));
            Write(Path.Combine(Outbox(Alice), ".cache", "x.txt"), "hidden");
            Write(Path.Combine(Outbox(Alice), ".hidden"), "hidden");
            if (!OperatingSystem.IsWindows())
            {
                File.CreateSymbolicLink(Path.Combine(Outbox(Alice), "link.txt"), "/etc/hostname");
                Directory.CreateSymbolicLink(Path.Combine(Outbox(Alice), "links"), "/etc");
            }

            return "done";
        });
        await files.StoreAsync(Alice, null, Incoming("same.txt", "x"), 0, "/outbox", Ct);
        await files.StoreAsync(Alice, null, Incoming("changed.txt", "x"), 0, "/outbox", Ct);

        var result = await RunAsync(host, In(Alice));

        Assert.Equal(
            [new OutboxFile("/outbox/changed.txt", 18), new OutboxFile("/outbox/new.txt", 5), new OutboxFile("/outbox/sub/deep.txt", 1)],
            result.OutboxFiles);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShouldListTheFilesItWrote_WhenTheRunFailsOrIsStopped(bool stopped)
    {
        IHost? host = null;
        (host, _) = Build(async (fs, ct) =>
        {
            await fs.WriteAllTextAsync("/outbox/half.txt", "x", ct);
            if (!stopped)
                throw new InvalidOperationException("the crew fails");
            var registry = host!.Services.GetRequiredService<CrewHostRegistry>();
            registry.RequestStop(Assert.Single(registry.Running).Id);
            await Task.Delay(Timeout.Infinite, ct);
            return "never";
        });

        var result = await RunAsync(host, In(Alice));

        Assert.NotEqual(HostedRunOutcome.Completed, result.Outcome);
        Assert.Equal("/outbox/half.txt", Assert.Single(result.OutboxFiles).Path);
    }

    // ----- reception -----

    [Fact]
    public async Task ShouldNeverWriteOverAFile_WhenANameIsTaken()
    {
        var (_, files) = Build();

        var stored = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(
            () => files.StoreAsync(Alice, null, Incoming(i % 2 == 0 ? "report.txt" : "REPORT.txt", $"{i}"), 0, ct: Ct), Ct)));

        Assert.Equal(8, stored.Select(s => s.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(
            Enumerable.Range(0, 8).Select(i => $"{i}"),
            Directory.GetFiles(Inbox(Alice)).Select(Read).Order(StringComparer.Ordinal));
        Assert.Empty(Directory.GetFiles(Path.Combine(Root, Alice.Key, "part")));
    }

    [Fact]
    public async Task ShouldKeepAnAttachmentOnce_WhenItsIdIsAlreadyKept()
    {
        var (_, files) = Build();

        var first = await files.StoreAsync(Alice, null, Incoming("a.txt", "a", id: "77"), 0, ct: Ct);
        var again = await files.StoreAsync(Alice, null, Incoming("a.txt", "a", id: "77"), 0, ct: Ct);
        File.Delete(Path.Combine(Inbox(Alice), "a.txt"));
        var after = await files.StoreAsync(Alice, null, Incoming("a.txt", "b", id: "77"), 0, ct: Ct);

        Assert.Equal(first, again);
        Assert.Null(after.Refused);
        Assert.Equal("b", Read(Assert.Single(Directory.GetFiles(Inbox(Alice)))));
    }

    [Theory]
    [InlineData("big.txt", "12345678901", null, "larger")]
    [InlineData("big.txt", "1", 11L, "larger")]
    [InlineData("a.zip", "1", null, "type")]
    public async Task ShouldRefuseTheFileAndLeaveNothing_WhenALimitForbidsIt(string name, string content, long? announced, string reason)
    {
        var (_, files) = Build(files: s =>
        {
            s["MaxFileBytes"] = 10;
            s["AllowedExtensions"] = s_extensions;
        });

        var refused = await files.StoreAsync(Alice, null, Incoming(name, content, announced: announced), 0, ct: Ct);

        Assert.Null(refused.Path);
        Assert.Contains(reason, refused.Refused!.Reason, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(Inbox(Alice)));
        Assert.Empty(Directory.GetFiles(Path.Combine(Root, Alice.Key, "part")));
    }

    [Theory]
    [InlineData("MaxConversationBytes")]
    [InlineData("MaxTotalBytes")]
    public async Task ShouldKeepWithinTheLimit_WhenFilesArriveAtOnceWithoutASize(string limit)
    {
        var (_, files) = Build(files: s => s[limit] = 100);
        using var arrived = new SemaphoreSlim(0);
        var open = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IncomingFile Slow(int i) => new($"{i}.txt", async ct =>
        {
            arrived.Release();
            await open.Task.WaitAsync(ct);
            return new MemoryStream(new byte[30]);
        });

        // Ten at once, of one conversation under its limit, of ten under the total one.
        var receptions = Enumerable.Range(0, 10)
            .Select(i => Task.Run(() => files.StoreAsync(limit == "MaxTotalBytes" ? new ConversationRef("discord", $"{i}") : Alice, null, Slow(i), 0, ct: Ct), Ct))
            .ToArray();
        await arrived.WaitAsync(Ct);
        open.SetResult();
        var stored = await Task.WhenAll(receptions);

        Assert.Equal(3, stored.Count(s => s.Path is not null));
        Assert.Equal(90, Directory.GetFiles(Root, "*.txt", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length));
        // What was reserved for the refused ones is given back.
        Assert.Null((await files.StoreAsync(Alice, null, Incoming("last.txt", "0123456789"), 0, ct: Ct)).Refused);
    }

    [Fact]
    public async Task ShouldCountAnAnnouncedSizeBeforeTheBytesCome_WhenAnotherFileArrives()
    {
        var (_, files) = Build(files: s => s["MaxTotalBytes"] = 100);
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var open = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = files.StoreAsync(Alice, null, new IncomingFile("a.txt", async ct =>
        {
            arrived.SetResult();
            await open.Task.WaitAsync(ct);
            return new MemoryStream(new byte[80]);
        }) { Size = 80 }, 0, ct: Ct);
        await arrived.Task.WaitAsync(Ct);

        var second = await files.StoreAsync(Bob, null, Incoming("b.txt", new string('x', 30)), 0, ct: Ct);
        open.SetResult();

        Assert.NotNull(second.Refused);
        Assert.Null((await first).Refused);
    }

    [Fact]
    public async Task ShouldRefuseTheFileAlone_WhenItsStreamFails()
    {
        var (_, files) = Build();

        var refused = await files.StoreAsync(Alice, null, new IncomingFile("a.txt", _ => throw new HttpRequestException("https://secret")), 0, ct: Ct);

        Assert.DoesNotContain("secret", refused.Refused!.Reason, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(Path.Combine(Root, Alice.Key, "part")));
    }

    [Fact]
    public async Task ShouldRefuseTheRun_WhenTheConversationIsOverItsLimit()
    {
        var ran = 0;
        var (host, _) = Build(async (fs, ct) =>
        {
            ran++;
            await fs.WriteAllTextAsync("/outbox/big.txt", new string('x', 200), ct);
            return "done";
        }, files: s => s["MaxConversationBytes"] = 100);

        var first = await RunAsync(host, In(Alice));
        var second = await RunAsync(host, In(Alice));
        var other = await RunAsync(host, In(Bob));

        Assert.Equal(HostedRunOutcome.Completed, first.Outcome);
        Assert.Equal(HostedRunOutcome.Failed, second.Outcome);
        Assert.Contains("limit", second.Message, StringComparison.Ordinal);
        Assert.Equal(HostedRunOutcome.Completed, other.Outcome);
        Assert.Equal(2, ran);
        Assert.Empty(host.Services.GetRequiredService<CrewHostRegistry>().Running);
    }

    // ----- retention -----

    [Fact]
    public async Task ShouldDeleteTheOldConversationsOnly_WhenItSweeps()
    {
        var (_, files) = Build();
        await files.StoreAsync(Alice, null, Incoming("a.txt", "a"), 0, ct: Ct);
        await files.StoreAsync(Bob, null, Incoming("b.txt", "b"), 0, ct: Ct);
        Age(Alice, TimeSpan.FromDays(8));
        Age(Bob, TimeSpan.FromDays(6));
        Directory.CreateDirectory(Path.Combine(Root, "run-old", "in"));
        Directory.SetLastWriteTimeUtc(Path.Combine(Root, "run-old"), DateTime.UtcNow.AddDays(-8));
        Directory.CreateDirectory(Path.Combine(Root, "run-new"));
        Directory.CreateDirectory(Path.Combine(Root, "not-ours"));
        Directory.SetLastWriteTimeUtc(Path.Combine(Root, "not-ours"), DateTime.UtcNow.AddDays(-8));

        await files.SweepAsync(Ct);

        Assert.Equal(
            new[] { Bob.Key, "not-ours", "run-new" }.Order(StringComparer.Ordinal),
            Directory.GetDirectories(Root).Select(d => Path.GetFileName(d)!).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ShouldDeleteALinkAndNotItsTarget_WhenItSweepsAConversation()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Symbolic links need a privilege.");
        var outside = Path.Combine(_harness.Root, "outside");
        Directory.CreateDirectory(outside);
        Write(Path.Combine(outside, "kept.txt"), "kept");
        var (_, files) = Build();
        await files.StoreAsync(Alice, null, Incoming("a.txt", "a"), 0, ct: Ct);
        Directory.CreateSymbolicLink(Path.Combine(Outbox(Alice), "link"), outside);
        Age(Alice, TimeSpan.FromDays(8));

        await files.SweepAsync(Ct);

        Assert.False(Directory.Exists(Path.Combine(Root, Alice.Key)));
        Assert.True(File.Exists(Path.Combine(outside, "kept.txt")));
    }

    [Fact]
    public async Task ShouldKeepAConversation_WhenItsRunIsInProgress()
    {
        ConversationFiles? files = null;
        var kept = false;
        var (host, built) = Build(async (_, ct) =>
        {
            Age(Alice, TimeSpan.FromDays(8));
            Task sweep;
            using (ExecutionContext.SuppressFlow())
                sweep = Task.Run(() => files!.SweepAsync(ct), ct);
            await sweep;
            kept = Directory.Exists(Path.Combine(Root, Alice.Key));
            return "done";
        });
        files = built;

        await RunAsync(host, In(Alice));
        Assert.True(kept);

        // The end of the run was an activity: the time starts again.
        await files.SweepAsync(Ct);
        Assert.True(Directory.Exists(Path.Combine(Root, Alice.Key)));
        Age(Alice, TimeSpan.FromDays(8));
        await files.SweepAsync(Ct);
        Assert.False(Directory.Exists(Path.Combine(Root, Alice.Key)));
    }

    [Fact]
    public async Task ShouldKeepAConversation_WhenARunOfItWaitsForAnAnswer()
    {
        var (host, files) = Build(
            orkeon: all =>
            {
                all["Checkpointing"] = new { Enabled = true, Store = "memory" };
                all["HumanInput"] = new { Pause = "Suspend" };
            },
            services: s => s.AddScoped<IAgentExecutionService>(sp => new AskingAgentExecutionService(
                sp.GetRequiredService<Orkeon.Application.Interfaces.Ports.IHumanInputProvider>(), "Which colour?")));

        var result = await RunAsync(host, In(Alice));
        Assert.Equal(HostedRunOutcome.WaitingForInput, result.Outcome);
        Age(Alice, TimeSpan.FromDays(30));
        await files.SweepAsync(Ct);
        Assert.True(Directory.Exists(Path.Combine(Root, Alice.Key)));

        // Answered, it waits no more.
        await host.Services.GetRequiredService<HostApiOptionalServices>().Input!.AnswerAsync(result.RunId!, null, "green", Ct);
        await files.SweepAsync(Ct);
        Assert.False(Directory.Exists(Path.Combine(Root, Alice.Key)));
    }

    [Fact]
    public async Task ShouldKeepAConversation_WhenTheStoreOfItsSuspendedRunCannotAnswer()
    {
        var (host, files) = Build();
        await files.StoreAsync(Alice, null, Incoming("a.txt", "a"), 0, ct: Ct);
        Age(Alice, TimeSpan.FromDays(30));
        var meta = JsonNode.Parse(Read(Meta(Alice)))!;
        meta["SuspendedRuns"] = new JsonArray("run-1");
        Write(Meta(Alice), meta.ToJsonString());
        using var failing = new ConversationFiles(
            host.Services.GetRequiredService<IOptions<OrkeonHostOptions>>(),
            host.Services.GetRequiredService<IFileSystemService>(),
            host.Services.GetRequiredService<IFileSystemScope>(),
            host.Services.GetRequiredService<FileSystemRegistry>(),
            new HostApiOptionalServices(
                checkpoints: new CheckpointManager(new FakeStateStore { Throws = true }, NullLogger<CheckpointManager>.Instance),
                checkpointing: Options.Create(new CheckpointingOptions { Enabled = true })),
            NullLogger<ConversationFiles>.Instance);

        await failing.SweepAsync(Ct);
        Assert.True(Directory.Exists(Path.Combine(Root, Alice.Key)));

        // Without a store no run can be resumed.
        await files.SweepAsync(Ct);
        Assert.False(Directory.Exists(Path.Combine(Root, Alice.Key)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("null")]
    public async Task ShouldStartTheTimeAgain_WhenTheRecordCannotBeRead(string? content)
    {
        var (_, files) = Build();
        await files.StoreAsync(Alice, null, Incoming("a.txt", "a"), 0, ct: Ct);
        if (content is null)
            File.Delete(Meta(Alice));
        else
            Write(Meta(Alice), content);

        await files.SweepAsync(Ct);

        Assert.True(File.Exists(Path.Combine(Inbox(Alice), "a.txt")));
        Age(Alice, TimeSpan.FromDays(8));
        await files.SweepAsync(Ct);
        Assert.False(Directory.Exists(Path.Combine(Root, Alice.Key)));
    }

    // ----- off -----

    [Fact]
    public async Task ShouldMountNothingAndMakeNoFolder_WhenFilesAreOff()
    {
        var denied = false;
        var (host, files) = Build(async (fs, ct) =>
        {
            denied = await DeniedAsync(() => fs.WriteAllTextAsync("/outbox/a.txt", "x", ct));
            return "done";
        }, enabled: false);

        var result = await RunAsync(host, In(Alice));

        Assert.Equal("done", result.Message);
        Assert.True(denied);
        Assert.Null(files);
        Assert.Empty(result.OutboxFiles);
        Assert.False(Directory.Exists(Root));
        Assert.DoesNotContain(host.Services.GetServices<IHostedService>(), s => s is ConversationRetentionService);
    }

    // ----- what a kill, an agent or an odd setting leaves -----

    private string Part(ConversationRef conversation)
    {
        var part = Path.Combine(Root, conversation.Key, "part", "left.part");
        Write(part, "half");
        return part;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShouldDeleteWhatAReceptionCutShortLeft_WhenARunOpensOrASweepPasses(bool run)
    {
        var (host, files) = Build((_, _) => Task.FromResult("done"));
        await files.StoreAsync(Alice, null, Incoming("a.txt", "a"), 0, ct: Ct);
        var part = Part(Alice);

        if (run)
            await RunAsync(host, In(Alice));
        else
            await files.SweepAsync(Ct);

        Assert.False(File.Exists(part));
        Assert.True(File.Exists(Path.Combine(Inbox(Alice), "a.txt")));
    }

    [Fact]
    public async Task ShouldStopTheConversationAlone_WhenAFolderOfItCannotBeListed()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess, "A mode forbids nothing here.");
        var (host, files) = Build((_, _) => Task.FromResult("done"));
        await files.StoreAsync(Alice, null, Incoming("a.txt", "a"), 0, ct: Ct);
        var locked = Directory.CreateDirectory(Path.Combine(Outbox(Alice), "locked")).FullName;
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(locked, UnixFileMode.None);

        var stopped = await RunAsync(host, In(Alice));
        var refused = await files.StoreAsync(Alice, null, Incoming("b.txt", "b"), 1, ct: Ct);
        var other = await RunAsync(host, In(Bob));

        Assert.Equal(HostedRunOutcome.Failed, stopped.Outcome);
        Assert.NotNull(refused.Refused);
        Assert.Equal("done", other.Message);

        // And retention deletes it all the same.
        Age(Alice, TimeSpan.FromDays(30));
        await files.SweepAsync(Ct);
        Assert.False(Directory.Exists(Path.Combine(Root, Alice.Key)));
    }

    [Fact]
    public async Task ShouldDeleteAFileNobodyMayMeasureThrough_WhenALinkInTheOutboxLeadsNowhere()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Symbolic links need a privilege.");
        var (host, files) = Build((_, _) => Task.FromResult("done"));
        await files.StoreAsync(Alice, null, Incoming("a.txt", "a"), 0, ct: Ct);
        File.CreateSymbolicLink(Path.Combine(Outbox(Alice), "gone.txt"), Path.Combine(_harness.Root, "nowhere"));

        Assert.Equal("done", (await RunAsync(host, In(Alice))).Message);
    }

    [Fact]
    public async Task ShouldSweepTheRunFolders_WhenTheRetentionIsLongerThanTime()
    {
        var (_, files) = Build(files: s => s["Retention"] = TimeSpan.MaxValue.ToString());
        Directory.CreateDirectory(Path.Combine(Root, "run-1"));

        await files.SweepAsync(Ct);

        Assert.True(Directory.Exists(Path.Combine(Root, "run-1")));
    }

    [Fact]
    public async Task ShouldMarkTheRunBeforeItWorks_WhenTheHostCouldBeKilledDuringIt()
    {
        var during = string.Empty;
        var (host, _) = Build((_, _) =>
        {
            during = Read(Meta(Alice));
            return Task.FromResult("done");
        });

        var result = await RunAsync(host, In(Alice));

        Assert.Contains(result.RunId!, during, StringComparison.Ordinal);
        Assert.DoesNotContain(result.RunId!, Read(Meta(Alice)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldKeepTheFileWhereItIsAsked_WhenItsIdIsKeptInAnotherFolder()
    {
        var (_, files) = Build();

        var first = await files.StoreAsync(Alice, null, Incoming("a.txt", "a", id: "7"), 0, ct: Ct);
        var second = await files.StoreAsync(Alice, null, Incoming("a.txt", "a", id: "7"), 0, "/outbox", Ct);
        var third = await files.StoreAsync(Alice, null, Incoming("a.txt", "a", id: "7"), 0, ct: Ct);

        Assert.Equal(["/inbox/a.txt", "/outbox/a.txt", "/inbox/a.txt"], new[] { first.Path, second.Path, third.Path });
    }

    [Fact]
    public async Task ShouldKeepTheFileForEachCrew_WhenTheyGrantOneFolderEach()
    {
        var (_, files) = Build(services: s => s.Configure<Orkeon.Infrastructure.Configuration.PathSecurityOptions>(o => o.AdditionalAllowedDirectories.Add(_harness.Root)));
        HostedCrewOptions Crew(string name)
        {
            var folder = Directory.CreateDirectory(Path.Combine(_harness.Root, name)).FullName;
            return new HostedCrewOptions { Name = name, Path = _harness.CrewPath, Mounts = [$"{folder}:/data:rw"] };
        }

        await files.StoreAsync(Alice, Crew("one"), Incoming("a.txt", "a", id: "7"), 0, "/data", Ct);
        var two = Crew("two");
        Write(Path.Combine(_harness.Root, "two", "a.txt"), "another file");
        var second = await files.StoreAsync(Alice, two, Incoming("a.txt", "a", id: "7"), 0, "/data", Ct);
        var without = await files.StoreAsync(Alice, null, Incoming("a.txt", "a", id: "7"), 0, "/data", Ct);

        Assert.Equal("/data/a (1).txt", second.Path);
        Assert.NotNull(without.Refused);
    }

    [Fact]
    public async Task ShouldDownloadNothing_WhenTheHostKeepsNoFileOfThisName()
    {
        var opened = 0;
        var (_, files) = Build();

        var stored = await files.StoreAsync(Alice, null, new IncomingFile("tool.exe", _ =>
        {
            opened++;
            return Task.FromResult(Content(""));
        }), 0, ct: Ct);

        Assert.NotNull(stored.Refused);
        Assert.Equal(0, opened);
    }

    [Fact]
    public async Task ShouldKeepTheOutboxItself_WhenAnAgentDeletesIt()
    {
        var denied = new List<bool>();
        var (host, _) = Build(async (fs, ct) =>
        {
            await fs.WriteAllTextAsync("/outbox/sub/a.txt", "a", ct);
            denied.Add(await DeniedAsync(() => fs.DeleteAsync("/outbox", recursive: true, ct)));
            denied.Add(await DeniedAsync(() => fs.DeleteAsync("/outbox/sub", recursive: true, ct)));
            await fs.WriteAllTextAsync("/outbox/b.txt", "b", ct);
            return "done";
        });

        var result = await RunAsync(host, In(Alice));
        Assert.Equal([true, false], denied);
        Assert.Equal("/outbox/b.txt", Assert.Single(result.OutboxFiles).Path);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public void ShouldAllowTheRoot_OnlyWhenTheWorkspaceDoesNotHoldIt(bool inside, int added)
    {
        var options = new Orkeon.Infrastructure.Configuration.PathSecurityOptions { DefaultWorkspaceRoot = inside ? _harness.Root : Path.Combine(_harness.Root, "elsewhere") };
        var before = options.AdditionalAllowedDirectories.Count;

        HostFilesStartup.Allow(options, Root);

        Assert.Equal(added, options.AdditionalAllowedDirectories.Count - before);
    }
}
