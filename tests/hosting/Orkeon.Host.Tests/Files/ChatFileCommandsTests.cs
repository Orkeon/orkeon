using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Host.Files;
using Orkeon.Host.Gateway;
using Orkeon.Host.Tests.Api;
using Orkeon.Host.Tests.Doubles;
using Orkeon.Infrastructure.Configuration;

namespace Orkeon.Host.Tests.Files;

/// <summary>The file commands of a conversation, over the folders and the mounts the daemon has.</summary>
public sealed class ChatFileCommandsTests : IDisposable
{
    private const string Declared = """

        files:
          - path: /output/open
            download: true
          - path: /output/drop
            upload: true
        """;

    private static readonly ConversationRef Alice = new("discord", "1001");

    private readonly HostApiHarness _harness = new();
    private readonly List<IHost> _hosts = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Root => Path.Combine(_harness.Root, "conversations");

    private string Outbox => Path.Combine(Root, Alice.Key, "out");

    private string Output => Path.Combine(_harness.Root, "output");

    public void Dispose()
    {
        foreach (var host in _hosts)
            host.Dispose();
        _harness.Dispose();
    }

    private (ChatFileCommands Commands, ConversationFiles Files) Build(
        string declared = Declared, Action<Dictionary<string, object?>>? files = null, params string[] mounts)
    {
        File.AppendAllText(_harness.CrewPath, declared);
        foreach (var folder in new[] { "open", "drop", "closed" })
            Directory.CreateDirectory(Path.Combine(Output, folder));
        Directory.CreateDirectory(Outbox);
        Directory.CreateDirectory(Path.Combine(Root, Alice.Key, "in"));
        var host = _harness.Build(
            new HeldLlmProvider("unused"),
            port: 1,
            configure: (root, api) =>
            {
                api["Enabled"] = false;
                var section = new Dictionary<string, object?> { ["Enabled"] = true, ["Root"] = Root };
                files?.Invoke(section);
                var settings = (Dictionary<string, object?>)((Dictionary<string, object?>)root["Orkeon"]!)["Host"]!;
                settings["Files"] = section;
                settings["Crews"] = new object[]
                {
                    new { Name = "support", Path = _harness.CrewPath, Mounts = (string[])[$"{Output}:/output:rw", .. mounts] },
                    new { Name = "other", Path = _harness.CrewPath },
                };
            },
            services: services => services.Configure<PathSecurityOptions>(o => o.AdditionalAllowedDirectories.Add(_harness.Root)));
        _hosts.Add(host);
        var kept = host.Services.GetRequiredService<ConversationFiles>();
        return (
            new ChatFileCommands(
                "discord",
                new ChatRoutes("support", new Dictionary<string, string> { ["7"] = "other" }),
                host.Services.GetRequiredService<CrewHostRegistry>().Crews,
                kept,
                host.Services.GetRequiredService<CrewFileFolders>()),
            kept);
    }

    private static CommandInvocation Invocation(string command, params (string Name, string Value)[] arguments) =>
        new(command, Alice.ConversationId, "alice")
        {
            RoomId = "5",
            Arguments = arguments.ToDictionary(a => a.Name, a => a.Value),
        };

    private static CommandInvocation Get(string path) => Invocation(ChatCommands.Get, ("path", path));

    private static CommandInvocation Put(string folder, FakeAttachmentSource source, string name = "a.txt", string id = "1") =>
        Invocation(ChatCommands.Put, ("folder", folder)) with
        {
            Attachments = [new InboundAttachment { Id = id, Name = name }],
            AttachmentSource = source,
        };

    private static async Task<string> TextAsync(OutboundFile file)
    {
        await using var stream = await file.OpenAsync(Ct);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(Ct);
    }

    private static void Write(string folder, string name, string text)
    {
        var path = Path.Combine(folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public async Task ShouldRefuse_WhenTheRoomIsNotKnown()
    {
        var (commands, _) = Build();
        Write(Outbox, "a.txt", "a");

        foreach (var invocation in new[] { Invocation(ChatCommands.Files), Get("/outbox/a.txt"), Put("/output/drop", new FakeAttachmentSource().With("1", "x")) })
        {
            var reply = await commands.AnswerAsync(invocation with { RoomId = null }, Ct);
            Assert.Empty(reply.Files);
            Assert.Contains("thread", reply.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("a.txt", reply.Text, StringComparison.Ordinal);

            // A thread whose parent the platform does not give is no thread of the default crew.
            var unresolved = await commands.AnswerAsync(invocation with { RoomId = null, ResolveRoomAsync = _ => Task.FromResult<string?>(null) }, Ct);
            Assert.Equal(reply.Text, unresolved.Text);
        }

        Assert.Empty(Directory.GetFiles(Path.Combine(Output, "drop")));
        Assert.Empty(await commands.SuggestAsync(Get("") with { RoomId = null, Focused = "path" }, Ct));
    }

    [Fact]
    public async Task ShouldAnswerForTheCrewOfTheRoom_WhenItIsResolvedLate()
    {
        var (commands, _) = Build();
        Write(Output, "open/a.txt", "a");

        var support = await commands.AnswerAsync(Get("/output/open/a.txt") with { RoomId = null, ResolveRoomAsync = _ => Task.FromResult<string?>("5") }, Ct);
        var other = await commands.AnswerAsync(Get("/output/open/a.txt") with { RoomId = null, ResolveRoomAsync = _ => Task.FromResult<string?>("7") }, Ct);

        Assert.Equal("a", await TextAsync(Assert.Single(support.Files)));
        Assert.Empty(other.Files);
    }

    [Fact]
    public async Task ShouldListWhatTheConversationHas()
    {
        var (commands, _) = Build();
        Write(Outbox, "report.csv", "12345");
        Write(Outbox, ".hidden", "x");
        Write(Path.Combine(Root, Alice.Key, "in"), "sent.txt", "x");
        Write(Output, "open/sub/b.txt", "b");
        Write(Output, "closed/c.txt", "c");

        var all = (await commands.AnswerAsync(Invocation(ChatCommands.Files), Ct)).Text;

        foreach (var shown in new[] { "`/outbox/report.csv` (5 bytes)", "`/inbox/sent.txt`", "`/output/open/sub/b.txt`", "`/output/drop`" })
            Assert.Contains(shown, all, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden", all, StringComparison.Ordinal);
        Assert.DoesNotContain("closed", all, StringComparison.Ordinal);

        var one = (await commands.AnswerAsync(Invocation(ChatCommands.Files, ("folder", "/output/open/")), Ct)).Text;
        Assert.Contains("b.txt", one, StringComparison.Ordinal);
        Assert.DoesNotContain("report.csv", one, StringComparison.Ordinal);

        var closed = (await commands.AnswerAsync(Invocation(ChatCommands.Files, ("folder", "/output/closed")), Ct)).Text;
        var absent = (await commands.AnswerAsync(Invocation(ChatCommands.Files, ("folder", "/output/nowhere")), Ct)).Text;
        Assert.Equal(closed, absent);
    }

    [Fact]
    public async Task ShouldFitOneMessage_WhenThereAreManyFiles()
    {
        var (commands, _) = Build();
        for (var i = 0; i < 80; i++)
            Write(Outbox, $"{new string('n', 80)}-{i}.txt", "x");

        var text = (await commands.AnswerAsync(Invocation(ChatCommands.Files), Ct)).Text;

        Assert.InRange(text.Length, 100, 2000);
        Assert.Contains("more", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldSayWhy_WhenTheDeclarationIsInvalid()
    {
        var (commands, _) = Build("\nfiles:\n  - path: /nowhere\n    download: true\n");
        Write(Outbox, "a.txt", "a");

        var text = (await commands.AnswerAsync(Invocation(ChatCommands.Files), Ct)).Text;

        Assert.Contains("/nowhere", text, StringComparison.Ordinal);
        Assert.Contains("`/outbox/a.txt`", text, StringComparison.Ordinal);
        Assert.Single((await commands.AnswerAsync(Get("/outbox/a.txt"), Ct)).Files);
    }

    [Fact]
    public async Task ShouldGiveAFileOfTheOutbox_WithItsNameAndSize()
    {
        var (commands, files) = Build();
        Write(Outbox, "sub/report.csv", "12345");

        var reply = await commands.AnswerAsync(Get(" /outbox/sub/report.csv "), Ct);

        var file = Assert.Single(reply.Files);
        Assert.Equal(("report.csv", 5L), (file.Name, file.Size));
        // Opened after the command's own mount set is gone, and the conversation is free while it is sent.
        await using var stream = await file.OpenAsync(Ct);
        Assert.True(await files.UseAsync(Alice, crew: null, (_, _) => Task.FromResult(true), Ct));
        Write(Outbox, "sub/report.csv", "rewritten while it is sent");
        using var reader = new StreamReader(stream);
        Assert.Equal("12345", await reader.ReadToEndAsync(Ct));
    }

    [Fact]
    public async Task ShouldSayOneThing_WhenAFileIsAbsentOrNotOpen()
    {
        var (commands, _) = Build();
        Write(Output, "closed/c.txt", "c");
        Write(Output, "open/.secret", "s");
        Write(Output, "drop/d.txt", "d");
        Write(Outbox, "folder/x.txt", "x");
        Write(Path.Combine(Root, Alice.Key, "in"), "sent.txt", "x");
        Write(Path.Combine(Root, new ConversationRef("discord", "1002").Key, "out"), "theirs.txt", "x");
        var absent = (await commands.AnswerAsync(Get("/outbox/absent.txt"), Ct)).Text;
        Assert.Contains("/files", absent, StringComparison.Ordinal);

        foreach (var path in new[]
        {
            "/output/closed/c.txt", "/output/open/../closed/c.txt", "/output/open/.secret", "/output/drop/d.txt", "/outbox/folder", "/outbox",
            "/outbox/../inbox/sent.txt", "/inbox/sent.txt", "/inbox/.conversation/meta.json", "/outbox/./folder/x.txt", "/outbox//folder/x.txt",
            "/outbox\\folder\\x.txt", "/crews/support.yaml", "/output/openx/c.txt", "/OUTBOX/folder/x.txt", "outbox/folder/x.txt", "",
            "/outbox/../../" + new ConversationRef("discord", "1002").Key + "/out/theirs.txt", "/outbox/a\0b",
        })
        {
            var reply = await commands.AnswerAsync(Get(path), Ct);
            Assert.True(reply.Files.Count == 0 && reply.Text == absent, path);
        }
    }

    [Fact]
    public async Task ShouldRefuseALink_OnTheFileOrOnTheWayToIt()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Links need a privilege on Windows.");
        var (commands, _) = Build();
        Write(Output, "closed/c.txt", "c");
        Directory.CreateSymbolicLink(Path.Combine(Output, "open", "way"), Path.Combine(Output, "closed"));
        File.CreateSymbolicLink(Path.Combine(Output, "open", "c.txt"), Path.Combine(Output, "closed", "c.txt"));
        File.CreateSymbolicLink(Path.Combine(Outbox, "crew"), _harness.CrewPath);

        foreach (var path in new[] { "/output/open/way/c.txt", "/output/open/c.txt", "/outbox/crew" })
            Assert.Empty((await commands.AnswerAsync(Get(path), Ct)).Files);

        Directory.Delete(Path.Combine(Output, "open"), recursive: true);
        Directory.CreateSymbolicLink(Path.Combine(Output, "open"), Path.Combine(Output, "closed"));
        Assert.Empty((await commands.AnswerAsync(Get("/output/open/c.txt"), Ct)).Files);
        Assert.DoesNotContain("c.txt", (await commands.AnswerAsync(Invocation(ChatCommands.Files), Ct)).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldRefuse_WhenTheFileBecomesALinkBeforeItIsOpened()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Links need a privilege on Windows.");
        var (commands, _) = Build();
        Write(Output, "open/a.txt", "a");
        Write(Output, "closed/c.txt", "c");
        var file = Assert.Single((await commands.AnswerAsync(Get("/output/open/a.txt"), Ct)).Files);

        File.Delete(Path.Combine(Output, "open", "a.txt"));
        File.CreateSymbolicLink(Path.Combine(Output, "open", "a.txt"), Path.Combine(Output, "closed", "c.txt"));

        await Assert.ThrowsAnyAsync<IOException>(() => file.OpenAsync(Ct));
    }

    [Fact]
    public async Task ShouldNotSend_WhenTheFileGrowsBeforeItIsOpened()
    {
        var (commands, _) = Build();
        Write(Outbox, "a.txt", "half");
        var file = Assert.Single((await commands.AnswerAsync(Get("/outbox/a.txt"), Ct)).Files);

        Write(Outbox, "a.txt", "half and the rest");

        await Assert.ThrowsAnyAsync<IOException>(() => file.OpenAsync(Ct));
    }

    [Fact]
    public async Task ShouldNotReachAnotherMount_UnderAnOpenedFolder()
    {
        var kept = Directory.CreateDirectory(Path.Combine(_harness.Root, "kept")).FullName;
        var nested = Directory.CreateDirectory(Path.Combine(_harness.Root, "nested")).FullName;
        Write(kept, "shown.txt", "s");
        Write(nested, "n.txt", "n");
        var (commands, _) = Build(
            "\nfiles:\n  - path: /kept\n    download: true\n", files: null, $"{kept}:/kept:rw", $"{nested}:/kept/nested:ro");

        Assert.Single((await commands.AnswerAsync(Get("/kept/shown.txt"), Ct)).Files);
        // Another mount under the folder was not declared.
        Assert.Empty((await commands.AnswerAsync(Get("/kept/nested/n.txt"), Ct)).Files);
        Assert.DoesNotContain("nested", (await commands.AnswerAsync(Invocation(ChatCommands.Files), Ct)).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldKeepAFileOnce_UnderAFreeName_WhenItIsLeft()
    {
        var (commands, _) = Build();
        Write(Output, "drop/a.txt", "theirs");
        var source = new FakeAttachmentSource().With("1", "mine");

        var first = await commands.AnswerAsync(Put("/output/drop/", source), Ct);
        var again = await commands.AnswerAsync(Put("/output/drop", source), Ct);

        Assert.Contains("`/output/drop/a (1).txt`", first.Text, StringComparison.Ordinal);
        Assert.Equal(first.Text, again.Text);
        Assert.Equal(["1"], source.Opened);
        Assert.Equal("theirs", await File.ReadAllTextAsync(Path.Combine(Output, "drop", "a.txt"), Ct));
        Assert.Equal("mine", await File.ReadAllTextAsync(Path.Combine(Output, "drop", "a (1).txt"), Ct));
    }

    [Fact]
    public async Task ShouldOpenNothing_WhenTheFolderTakesNoFile()
    {
        var (commands, _) = Build();
        var source = new FakeAttachmentSource().With("1", "x");
        var texts = new HashSet<string>(StringComparer.Ordinal);

        foreach (var folder in new[] { "/output/open", "/output/closed", "/output/nowhere", "/inbox", "/outbox", "/output/drop/sub", "/output/drop/../closed", "" })
            texts.Add((await commands.AnswerAsync(Put(folder, source), Ct)).Text);

        Assert.Single(texts);
        Assert.Empty(source.Opened);
    }

    [Fact]
    public async Task ShouldSayWhy_WhenTheLeftFileIsOverALimit()
    {
        var (commands, _) = Build(files: s => s["MaxFileBytes"] = 3);

        var reply = await commands.AnswerAsync(Put("/output/drop", new FakeAttachmentSource().With("1", "four")), Ct);

        Assert.Contains("larger than 3 bytes", reply.Text, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(Path.Combine(Output, "drop")));
    }

    [Fact]
    public async Task ShouldRefuseAFolderBecomeALink_WhenAFileIsLeft()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Links need a privilege on Windows.");
        var (commands, _) = Build();
        Directory.Delete(Path.Combine(Output, "drop"));
        Directory.CreateSymbolicLink(Path.Combine(Output, "drop"), Path.Combine(Output, "closed"));

        await commands.AnswerAsync(Put("/output/drop", new FakeAttachmentSource().With("1", "x")), Ct);

        Assert.Empty(Directory.GetFiles(Path.Combine(Output, "closed")));
    }

    [Fact]
    public async Task ShouldServeEveryCommand_WhileARunIsGoing()
    {
        var (commands, files) = Build();
        Write(Outbox, "a.txt", "a");
        var run = await files.OpenRunAsync(new HostedConversation { Channel = Alice.Channel, ConversationId = Alice.ConversationId, PrepareAsync = _ => Task.FromResult(new PreparedRun("go")) }, "run-1", Ct);
        try
        {
            Assert.Contains("a.txt", (await commands.AnswerAsync(Invocation(ChatCommands.Files), Ct)).Text, StringComparison.Ordinal);
            Assert.Equal("a", await TextAsync(Assert.Single((await commands.AnswerAsync(Get("/outbox/a.txt"), Ct)).Files)));
            Assert.Contains("/output/drop/a.txt", (await commands.AnswerAsync(Put("/output/drop", new FakeAttachmentSource().With("1", "x")), Ct)).Text, StringComparison.Ordinal);
        }
        finally
        {
            await run.CloseAsync(suspended: false);
        }
    }

    [Fact]
    public async Task ShouldSayItIsBeingWritten_WhenTheFileIsHeld()
    {
        var (commands, _) = Build();
        Write(Outbox, "a.txt", "a");
        await using var held = new FileStream(Path.Combine(Outbox, "a.txt"), FileMode.Open, FileAccess.Write, FileShare.None);

        var file = Assert.Single((await commands.AnswerAsync(Get("/outbox/a.txt"), Ct)).Files);

        await Assert.ThrowsAnyAsync<IOException>(() => file.OpenAsync(Ct));
    }

    [Fact]
    public async Task ShouldSuggestWhatIsOpen_ShortEnoughToChoose()
    {
        var (commands, _) = Build();
        for (var i = 0; i < 30; i++)
            Write(Outbox, $"file-{i:00}.txt", "x");
        Write(Outbox, new string('l', 95) + ".txt", "x");
        Write(Output, "open/one.csv", "x");
        Write(Output, "closed/two.csv", "x");

        var all = await commands.SuggestAsync(Get("") with { Focused = "path" }, Ct);
        var typed = await commands.SuggestAsync(Get("CSV") with { Focused = "path" }, Ct);
        var tooLong = await commands.SuggestAsync(Get("lll") with { Focused = "path" }, Ct);
        var folders = await commands.SuggestAsync(Invocation(ChatCommands.Files, ("folder", "")) with { Focused = "folder" }, Ct);
        var uploads = await commands.SuggestAsync(Invocation(ChatCommands.Put, ("folder", "")) with { Focused = "folder" }, Ct);

        Assert.Equal(25, all.Count);
        Assert.Equal(["/output/open/one.csv"], typed);
        Assert.Empty(tooLong);
        Assert.Equal(["/outbox", "/inbox", "/output/open"], folders);
        Assert.Equal(["/output/drop"], uploads);
        // The long one is given to who types it.
        Assert.Single((await commands.AnswerAsync(Get("/outbox/" + new string('l', 95) + ".txt"), Ct)).Files);
    }

    [Fact]
    public async Task ShouldAskNothingOfThePlatform_WhenTheSenderIsNotAllowed()
    {
        var (commands, files) = Build();
        Write(Outbox, "a.txt", "a");
        var authorizer = new AllowListChatAuthorizer(["alice"]);
        var gateway = new ChatGateway(new ScriptedRunner(), new ThreadIsRunRouter("support"), authorizer, _hosts[0].Services.GetRequiredService<CrewHostRegistry>(), NullLogger<ChatGateway>.Instance, files);
        var asked = 0;
        CommandInvocation By(string sender) => new(ChatCommands.Get, Alice.ConversationId, sender)
        {
            Arguments = new Dictionary<string, string> { ["path"] = "/outbox/a.txt" },
            Focused = "path",
            ResolveRoomAsync = _ =>
            {
                asked++;
                return Task.FromResult<string?>("5");
            },
        };

        var refused = await ChatChannelService.HandleCommandAsync(gateway, authorizer, By("stranger"), Ct, commands);
        var none = await ChatChannelService.SuggestAsync(authorizer, By("stranger"), commands, Ct);
        Assert.Equal((0, 0, 0), (asked, refused.Files.Count, none.Count));
        Assert.Contains("not authorized", refused.Text, StringComparison.Ordinal);

        Assert.Single((await ChatChannelService.HandleCommandAsync(gateway, authorizer, By("alice"), Ct, commands)).Files);
        Assert.Equal(["/outbox/a.txt"], await ChatChannelService.SuggestAsync(authorizer, By("alice") with { Arguments = new Dictionary<string, string> { ["path"] = "a" } }, commands, Ct));

        var off = await ChatChannelService.HandleCommandAsync(gateway, authorizer, By("alice"), Ct);
        Assert.Contains("off", off.Text, StringComparison.Ordinal);
        Assert.Empty(await ChatChannelService.SuggestAsync(authorizer, By("alice"), files: null, Ct));
    }
}
