using System.Text;
using Discord;
using Discord.WebSocket;
using Orkeon.Host.Gateway;
using Orkeon.Host.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Host.Tests;

/// <summary>Files on their way to Discord, and the deferred commands that carry them.</summary>
public sealed class DiscordFilesTests
{
    private readonly List<string> _opened = [];
    private readonly List<string[]> _posts = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private OutboundFile File(string name, long size = 1, Func<Stream>? open = null) => new()
    {
        Name = name,
        Size = size,
        OpenAsync = _ =>
        {
            _opened.Add(name);
            return Task.FromResult(open?.Invoke() ?? Content(name));
        },
    };

    private static Stream Content(string text)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return stream;
    }

    private Task Post(IReadOnlyList<FileAttachment> attachments)
    {
        // Read while it lasts: the streams are gone once the message has left.
        _posts.Add([.. attachments.Select(a =>
        {
            using var reader = new StreamReader(a.Stream, leaveOpen: true);
            return $"{a.FileName}:{reader.ReadToEnd()}";
        })]);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ShouldSplit_ByCount()
    {
        var results = await DiscordFiles.SendAsync([.. Enumerable.Range(0, 23).Select(i => File($"f{i}"))], 100, Post, Ct);

        Assert.Equal([10, 10, 3], _posts.Select(p => p.Length));
        Assert.Equal("f22:f22", _posts[2][2]);
        Assert.All(results, r => Assert.Equal(FileSendOutcome.Sent, r.Outcome));
        Assert.Equal(Enumerable.Range(0, 23).Select(i => $"f{i}"), results.Select(r => r.Name));
    }

    [Fact]
    public async Task ShouldSplit_ByTheBytesOfTheRequest()
    {
        const long third = DiscordFiles.RequestBytes / 2 - 1;

        await DiscordFiles.SendAsync([File("a", third), File("b", third), File("c", third)], long.MaxValue, Post, Ct);

        Assert.Equal([2, 1], _posts.Select(p => p.Length));
    }

    [Fact]
    public async Task ShouldNameWhatIsTooLarge_WithoutOpeningIt()
    {
        var results = await DiscordFiles.SendAsync([File("a", 11), File("b", 10), File("c", DiscordFiles.RequestBytes + 1)], 10, Post, Ct);
        var bound = await DiscordFiles.SendAsync([File("d", DiscordFiles.RequestBytes + 1)], long.MaxValue, Post, Ct);

        Assert.Equal([FileSendOutcome.TooLarge, FileSendOutcome.Sent, FileSendOutcome.TooLarge], results.Select(r => r.Outcome));
        Assert.Equal(FileSendOutcome.TooLarge, Assert.Single(bound).Outcome);
        Assert.Contains("10 bytes", results[0].Reason, StringComparison.Ordinal);
        Assert.Equal(["b"], _opened);
    }

    [Fact]
    public async Task ShouldSendTheOthers_WhenOneCannotBeOpened()
    {
        var results = await DiscordFiles.SendAsync([File("a", open: () => throw new IOException("/srv/secret")), File("b")], 100, Post, Ct);

        Assert.Equal([FileSendOutcome.Refused, FileSendOutcome.Sent], results.Select(r => r.Outcome));
        Assert.DoesNotContain("secret", results[0].Reason, StringComparison.Ordinal);
        Assert.Equal("b:b", Assert.Single(Assert.Single(_posts)));
    }

    [Fact]
    public async Task ShouldOpenABatchOnlyWhenItLeaves_AndDisposeIt()
    {
        var streams = new List<TrackedStream>();
        Stream Tracked()
        {
            var stream = new TrackedStream();
            streams.Add(stream);
            return stream;
        }

        await DiscordFiles.SendAsync(
            [.. Enumerable.Range(0, 12).Select(i => File($"f{i}", open: Tracked))],
            100,
            a =>
            {
                Assert.All(a, f => Assert.True(f.Stream.CanRead));
                Assert.Equal(a.Count, streams.Count(s => !s.Disposed));
                return Post(a);
            },
            Ct);

        Assert.Equal(2, _posts.Count);
        Assert.All(streams, s => Assert.True(s.Disposed));
    }

    [Fact]
    public async Task ShouldAnswerForEachFile_WhenDiscordRefusesTheMessage()
    {
        var results = await DiscordFiles.SendAsync([File("a"), File("b")], 100, _ => throw new HttpRequestException("token=secret"), Ct);

        Assert.All(results, r => Assert.Equal(FileSendOutcome.Refused, r.Outcome));
        Assert.All(results, r => Assert.DoesNotContain("secret", r.Reason, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ShouldMentionNobody_WhenFilesArePosted()
    {
        var channel = StubDiscord.Of<IMessageChannel>();
        StubDiscord.OnCall(channel, (_, args) => Post([.. (IEnumerable<FileAttachment>)args[0]!]));

        await DiscordResponder.PostFilesAsync(channel, 100, [File("@everyone.txt")], Ct);

        var (method, arguments) = Assert.Single(StubDiscord.CallsOf(channel));
        Assert.Equal("SendFilesAsync", method);
        Assert.Same(AllowedMentions.None, Assert.Single(arguments.OfType<AllowedMentions>()));
        Assert.Single(Assert.Single(_posts));
    }

    [Fact]
    public async Task ShouldRefuseEachFile_WhenTheConversationIsNoChannel()
    {
        await using var client = new DiscordSocketClient();
        var responder = new DiscordResponder(client, new RecordingLogger());
        var message = new InboundMessage { Channel = "discord", ConversationId = "no id", SenderId = "1", Text = "", ReceivedAt = DateTimeOffset.UnixEpoch };

        var results = await responder.SendFilesAsync(message, [File("a"), File("b")], Ct);

        Assert.Equal(["a", "b"], results.Select(r => r.Name));
        Assert.All(results, r => Assert.Equal(FileSendOutcome.Refused, r.Outcome));
        Assert.Empty(_opened);
    }

    private static IDiscordInteraction Interaction(ulong? channel = 99, int limit = 100) => StubDiscord.Of<IDiscordInteraction>(
        ("ChannelId", channel), ("AttachmentSizeLimit", (ulong)limit), ("User", StubDiscord.Of<IUser>(("Id", 42UL))));

    [Fact]
    public async Task ShouldDeferPrivately_BeforeTheAnswerIsMade()
    {
        var interaction = Interaction();
        var before = -1;

        await DiscordChannel.AnswerDeferredAsync(
            interaction,
            () =>
            {
                before = StubDiscord.CallsOf(interaction).Count;
                return Task.FromResult(new CommandReply("words"));
            },
            Ct);

        var calls = StubDiscord.CallsOf(interaction);
        Assert.Equal(1, before);
        Assert.Equal(["DeferAsync", "FollowupAsync"], calls.Select(c => c.Method));
        Assert.Contains(true, calls[0].Arguments);
        Assert.Contains("words", calls[1].Arguments);
        Assert.Contains(true, calls[1].Arguments);
        Assert.Same(AllowedMentions.None, Assert.Single(calls[1].Arguments.OfType<AllowedMentions>()));
    }

    [Fact]
    public async Task ShouldFollowUpWithTheFiles_AndNameWhatDidNotLeave()
    {
        var interaction = Interaction();
        StubDiscord.OnCall(interaction, (method, args) =>
        {
            if (method == "FollowupWithFilesAsync")
                Post([.. (IEnumerable<FileAttachment>)args[0]!]);
        });

        await DiscordChannel.AnswerDeferredAsync(
            interaction, () => Task.FromResult(new CommandReply("here", [File("a"), File("big", 101)])), Ct);

        var calls = StubDiscord.CallsOf(interaction);
        Assert.Equal(["DeferAsync", "FollowupWithFilesAsync", "FollowupAsync"], calls.Select(c => c.Method));
        Assert.Contains("here", calls[1].Arguments);
        Assert.All(calls.Skip(1), c => Assert.Same(AllowedMentions.None, Assert.Single(c.Arguments.OfType<AllowedMentions>())));
        Assert.All(calls.Skip(1), c => Assert.Contains(true, c.Arguments));
        Assert.Contains("`big`", Assert.Single(calls[2].Arguments.OfType<string>()), StringComparison.Ordinal);
        Assert.Equal("a:a", Assert.Single(Assert.Single(_posts)));
    }

    [Fact]
    public async Task ShouldStillAnswer_WhenTheCommandFails()
    {
        var interaction = Interaction();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DiscordChannel.AnswerDeferredAsync(interaction, () => throw new InvalidOperationException("/srv/secret"), Ct));

        var calls = StubDiscord.CallsOf(interaction);
        Assert.Equal(["DeferAsync", "FollowupAsync"], calls.Select(c => c.Method));
        Assert.DoesNotContain("secret", Assert.Single(calls[1].Arguments.OfType<string>()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldResolveTheRoomByRest_WhenTheThreadIsNotCached()
    {
        Assert.Equal(5UL, await DiscordChannel.RoomOfAsync(99, _ => 5, _ => throw new InvalidOperationException("not asked")));
        Assert.Equal(6UL, await DiscordChannel.RoomOfAsync(99, _ => null, id => Task.FromResult<ulong?>(id == 99 ? 6 : null)));
        Assert.Null(await DiscordChannel.RoomOfAsync(99, _ => null, _ => Task.FromResult<ulong?>(null)));
        Assert.Null(await DiscordChannel.RoomOfAsync(99, _ => null, _ => throw new HttpRequestException("unknown channel")));
    }

    [Fact]
    public async Task ShouldCarryTheOptionsAndTheFile_ButNoRoomYet()
    {
        var attachment = StubDiscord.Of<IAttachment>(
            ("Id", 7UL), ("Filename", "a.txt"), ("ContentType", "text/plain"), ("Size", 3), ("Url", "https://cdn.discordapp.com/a/7"));
        using var handler = new StubHttpHandler();
        using var http = new HttpClient(handler);

        var invocation = DiscordChannel.InvocationOf(
            Interaction(), "put", [("file", attachment), ("folder", "/output")], (_, _) => Task.FromResult<ulong?>(5), http)!;

        Assert.Equal(("put", "99", "42", null), (invocation.CommandName, invocation.ConversationId, invocation.SenderId, invocation.RoomId));
        Assert.Equal("/output", Assert.Single(invocation.Arguments).Value);
        Assert.Equal(new InboundAttachment { Id = "7", Name = "a.txt", ContentType = "text/plain", Size = 3 }, Assert.Single(invocation.Attachments));
        Assert.Equal("5", await invocation.ResolveRoomAsync!(Ct));
        await using var stream = await invocation.AttachmentSource!.OpenAsync(invocation.Attachments[0], Ct);
        Assert.Equal("https://cdn.discordapp.com/a/7", Assert.Single(handler.Requests).Address);
        Assert.Null(DiscordChannel.InvocationOf(Interaction(channel: null), "put", [], (_, _) => Task.FromResult<ulong?>(5)));
    }

    [Fact]
    public void ShouldRegisterTheFileCommands_OnlyWhenFilesAreOn()
    {
        Assert.Equal(["status", "stop"], DiscordChannel.SlashCommands().Select(c => c.Name.Value));

        var commands = DiscordChannel.SlashCommands(files: true);

        Assert.Equal(["status", "stop", "files", "get", "put"], commands.Select(c => c.Name.Value));
        var options = commands.Where(c => c.Options.IsSpecified).ToDictionary(c => c.Name.Value, c => c.Options.Value);
        Assert.Equal(("folder", true, false), Shape(options["files"][0]));
        Assert.Equal(("path", true, true), Shape(options["get"][0]));
        Assert.Equal((ApplicationCommandOptionType.Attachment, "file"), (options["put"][0].Type, options["put"][0].Name));
        Assert.Equal(("folder", true, true), Shape(options["put"][1]));

        static (string, bool, bool) Shape(ApplicationCommandOptionProperties option) =>
            (option.Name, option.IsAutocomplete, option.IsRequired ?? false);
    }

    private sealed class TrackedStream : MemoryStream
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
