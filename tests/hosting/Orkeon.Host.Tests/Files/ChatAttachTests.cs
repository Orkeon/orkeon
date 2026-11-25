using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Execution;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Host.Files;
using Orkeon.Host.Gateway;
using Orkeon.Host.Tests.Api;
using Orkeon.Host.Tests.Doubles;
using Orkeon.Hosting.Conversations;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Host.Tests.Files;

/// <summary>An agent sends files to the conversation of its run: which files, how many, and to whom.</summary>
public sealed class ChatAttachTests : IDisposable
{
    private const string Declared = """

        files:
          - path: /output/open
            download: true
          - path: /output/drop
            upload: true
        """;

    private static readonly ConversationRef Alice = new("discord", "1001");
    private static readonly ConversationRef Bob = new("discord", "1002");

    private readonly HostApiHarness _harness = new();
    private readonly RecordingFileSender _sender = new();
    private Func<CancellationToken, Task<string>> _work = _ => Task.FromResult("done");
    private IHost _host = null!; // Built by each test.

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Root => Path.Combine(_harness.Root, "conversations");

    private string Output => Path.Combine(_harness.Root, "output");

    public void Dispose()
    {
        _host?.Dispose();
        _harness.Dispose();
    }

    private void Build(Action<Dictionary<string, object?>>? files = null, bool enabled = true)
    {
        File.AppendAllText(_harness.CrewPath, Declared);
        foreach (var folder in new[] { "open", "drop", "closed" })
            Directory.CreateDirectory(Path.Combine(Output, folder));
        _host = _harness.Build(
            new HeldLlmProvider("unused"),
            port: 1,
            configure: (root, api) =>
            {
                api["Enabled"] = false;
                var section = new Dictionary<string, object?> { ["Enabled"] = enabled, ["Root"] = Root };
                files?.Invoke(section);
                var settings = (Dictionary<string, object?>)((Dictionary<string, object?>)root["Orkeon"]!)["Host"]!;
                settings["Files"] = section;
                settings["Crews"] = new object[] { new { Name = "support", Path = _harness.CrewPath, Mounts = (string[])[$"{Output}:/output:rw"], Profile = new { MaxConcurrentRuns = 4 } } };
            },
            services: s =>
            {
                s.Configure<PathSecurityOptions>(o => o.AdditionalAllowedDirectories.Add(_harness.Root));
                s.AddScoped<IAgentExecutionService>(_ => new ScriptedAgentExecutionService(ct => _work(ct)));
            });
    }

    private Task<HostedRunResult> RunAsync(Func<CancellationToken, Task<string>> work)
    {
        _work = work;
        return RunAsync(Alice, _sender);
    }

    private Task<HostedRunResult> RunAsync(ConversationRef conversation, RecordingFileSender sender, Func<string, Task>? onStarted = null) =>
        _host.Services.GetRequiredService<ICrewRunner>().RunAsync("support", "go", "test", onStarted: onStarted, conversation: new HostedConversation
        {
            Channel = conversation.Channel,
            ConversationId = conversation.ConversationId,
            PrepareAsync = _ => Task.FromResult(new PreparedRun("go")),
            SendFilesAsync = sender.SendAsync,
        });

    private IFileSystemService Fs => _host.Services.GetRequiredService<IFileSystemService>();

    private async Task WriteAsync(params string[] paths)
    {
        foreach (var path in paths)
            await Fs.WriteAllTextAsync(path, "text of " + path, Ct);
    }

    private static async Task<ToolCallResponse> AttachAsync(CancellationToken ct, params string[] paths)
    {
        using var tool = new ChatAttachTool();
        return await tool.CallAsync(new ToolCallRequest("chat_attach", new Dictionary<string, object?> { ["paths"] = paths.ToList() }), ct);
    }

    private static string Shown(ToolCallResponse response) => $"{response.Error}{JsonSerializer.Serialize(response.Result)}";

    // ----- where there is a conversation -----

    [Fact]
    public async Task ShouldSendToTheConversation_AndSayWhatBecameOfEachFile()
    {
        Build();
        ToolCallResponse? answer = null;

        var result = await RunAsync(async ct =>
        {
            await WriteAsync("/outbox/a.txt", "/output/open/b.txt");
            answer = await AttachAsync(ct, "/outbox/a.txt", "/output/open/b.txt", "/outbox/none.txt");
            return "done";
        });

        Assert.True(result.Succeeded);
        Assert.Equal(["a.txt=text of /outbox/a.txt", "b.txt=text of /output/open/b.txt"], Assert.Single(_sender.Messages));
        Assert.True(answer!.Success, answer.Error);
        var shown = Shown(answer);
        Assert.Contains("\"sent\"", shown, StringComparison.Ordinal);
        Assert.Contains("/outbox/none.txt", shown, StringComparison.Ordinal);
        Assert.Contains("\"refused\"", shown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldNameAtTheEndOnlyWhatTheConversationHasNot()
    {
        Build();

        var result = await RunAsync(async ct =>
        {
            await WriteAsync("/outbox/sent.txt", "/outbox/changed.txt", "/outbox/kept.txt", "/outbox/offered.txt", "/outbox/rewritten.txt");
            await AttachAsync(ct, "/outbox/sent.txt", "/outbox/changed.txt", "/outbox/rewritten.txt");
            _sender.Result = f => new FileSendResult(f.Name, FileSendOutcome.AwaitingConsent);
            await AttachAsync(ct, "/outbox/offered.txt");
            await Fs.WriteAllTextAsync("/outbox/changed.txt", "another, longer text than what was sent before", ct);
            await RewriteAsync("/outbox/rewritten.txt", ct);
            return "done";
        });

        Assert.Equal(["/outbox/changed.txt", "/outbox/kept.txt", "/outbox/offered.txt", "/outbox/rewritten.txt"], result.OutboxFiles.Select(f => f.Path));
    }

    [Fact]
    public async Task ShouldRefuse_WhenThereIsNoConversation()
    {
        Build();
        ToolCallResponse? answer = null;
        _work = async ct =>
        {
            await WriteAsync("/outbox/a.txt");
            answer = await AttachAsync(ct, "/outbox/a.txt");
            return "done";
        };

        await _host.Services.GetRequiredService<ICrewRunner>().RunAsync("support", "go", "api");
        var outside = await AttachAsync(Ct, "/outbox/a.txt");

        Assert.Equal(ChatAttachTool.NoConversation, answer!.Error);
        Assert.Equal(ChatAttachTool.NoConversation, outside.Error);
        Assert.False(answer.Success);
    }

    [Fact]
    public async Task ShouldRefuse_WhenTheHostKeepsNoFiles()
    {
        Build(enabled: false);
        ToolCallResponse? answer = null;

        await RunAsync(async ct =>
        {
            answer = await AttachAsync(ct, "/output/open/b.txt");
            return "done";
        });

        Assert.Equal(ChatAttachTool.NoConversation, answer!.Error);
        Assert.Empty(_sender.Messages);
    }

    [Fact]
    public async Task ShouldReachTheWholeRun_AndNothingAfterIt()
    {
        Build();
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<ToolCallResponse>? late = null;

        await RunAsync(async ct =>
        {
            await WriteAsync("/outbox/a.txt");
            var child = await Task.Run(() => AttachAsync(ct, "/outbox/a.txt"), ct);
            Assert.True(child.Success, child.Error);
            late = Task.Run(async () =>
            {
                await go.Task;
                return await AttachAsync(CancellationToken.None, "/outbox/a.txt");
            }, CancellationToken.None);
            return "done";
        });
        go.SetResult();

        Assert.Equal(ChatAttachTool.NoConversation, (await late!.WaitAsync(Polling.DefaultTimeout, Ct)).Error);
        Assert.Single(_sender.Messages);
        Assert.Null(ConversationScope.Current);
    }

    [Fact]
    public async Task ShouldKeepTwoRunsApart()
    {
        Build();
        var bob = new RecordingFileSender();
        using var both = new Barrier(2);
        _work = async ct =>
        {
            await Fs.WriteAllTextAsync("/outbox/a.txt", "mine", ct);
            await Task.Run(() => both.SignalAndWait(Polling.DefaultTimeout, ct), ct);
            for (var i = 0; i < 3; i++)
                await AttachAsync(ct, "/outbox/a.txt");
            return "done";
        };

        await Task.WhenAll(RunAsync(Alice, _sender), RunAsync(Bob, bob));

        Assert.Equal(3, _sender.Messages.Count);
        Assert.Equal(3, bob.Messages.Count);
    }

    [Fact]
    public async Task ShouldSendEachRunsOwnFile()
    {
        Build();
        var bob = new RecordingFileSender();
        var turn = 0;
        _work = async ct =>
        {
            await Fs.WriteAllTextAsync("/outbox/a.txt", Interlocked.Increment(ref turn).ToString(System.Globalization.CultureInfo.InvariantCulture), ct);
            await AttachAsync(ct, "/outbox/a.txt");
            return "done";
        };

        await RunAsync(Alice, _sender);
        await RunAsync(Bob, bob);

        Assert.Equal("a.txt=1", Assert.Single(Assert.Single(_sender.Messages)));
        Assert.Equal("a.txt=2", Assert.Single(Assert.Single(bob.Messages)));
    }

    // ----- which files -----

    [Theory]
    [InlineData("/inbox/in.txt")]
    [InlineData("/output/closed/c.txt")]
    [InlineData("/output/drop/d.txt")]
    [InlineData("/output/open/../closed/c.txt")]
    [InlineData("/output/open/.hidden")]
    [InlineData("/crews/support/support.yaml")]
    [InlineData("/outbox")]
    [InlineData("outbox/a.txt")]
    [InlineData("/outbox/nothing.txt")]
    [InlineData("")]
    public async Task ShouldAnswerAlike_WhatIsNotOpenAndWhatIsNotThere(string path)
    {
        Build();
        await _host.Services.GetRequiredService<ConversationFiles>()
            .StoreAsync(Alice, null, new IncomingFile("in.txt", _ => Task.FromResult<Stream>(new MemoryStream([1]))), 0, ct: Ct);
        ToolCallResponse? answer = null;

        await RunAsync(async ct =>
        {
            await WriteAsync("/output/closed/c.txt", "/output/drop/d.txt", "/output/open/.hidden");
            answer = await AttachAsync(ct, path);
            return "done";
        });

        Assert.False(answer!.Success);
        Assert.Contains("no such file", answer.Error, StringComparison.Ordinal);
        Assert.Empty(_sender.Messages);
    }

    [Fact]
    public async Task ShouldNotFollowALink()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Symbolic links need a privilege.");
        Build();
        await File.WriteAllTextAsync(Path.Combine(Output, "closed", "c.txt"), "closed", Ct);
        File.CreateSymbolicLink(Path.Combine(Output, "open", "link.txt"), Path.Combine(Output, "closed", "c.txt"));
        ToolCallResponse? answer = null;

        await RunAsync(async ct =>
        {
            answer = await AttachAsync(ct, "/output/open/link.txt");
            return "done";
        });

        Assert.False(answer!.Success);
        Assert.Empty(_sender.Messages);
    }

    private async Task<ToolCallResponse> AttachAsAsync(string scope, string[] paths, CancellationToken ct)
    {
        var resolved = AgentFileSystemScope.Resolve("narrow", [new FileSystemScopeEntry(scope)], Fs.GetAvailableMounts());
        await using var agent = AgentRunScope.Create("1", "narrow", null, resolved);
        using var entered = agent.Enter();
        return await AttachAsync(ct, paths);
    }

    [Fact]
    public async Task ShouldKeepToTheScopeOfTheAgent()
    {
        Build();
        ToolCallResponse? outside = null;
        ToolCallResponse? inside = null;
        ToolCallResponse? outbox = null;

        await RunAsync(async ct =>
        {
            await WriteAsync("/outbox/a.txt", "/output/open/b.txt");
            outside = await AttachAsAsync("/output/closed", ["/outbox/a.txt", "/output/open/b.txt"], ct);
            inside = await AttachAsAsync("/output/open", ["/output/open/b.txt"], ct);
            outbox = await AttachAsAsync("/outbox", ["/outbox/a.txt", "/output/open/b.txt"], ct);
            return "done";
        });

        Assert.False(outside!.Success);
        Assert.Contains("no such file", outside.Error, StringComparison.Ordinal);
        // The scope does not hide the crew's declaration: only the files it leaves out.
        Assert.True(inside!.Success, inside.Error);
        Assert.True(outbox!.Success, outbox.Error);
        Assert.Equal([["b.txt=text of /output/open/b.txt"], ["a.txt=text of /outbox/a.txt"]], _sender.Messages);
    }

    [Fact]
    public async Task ShouldKeepToTheScope_WhenTheChannelOpensTheFileElsewhere()
    {
        Build();
        var seen = string.Empty;
        ToolCallResponse? answer = null;
        IReadOnlyList<OutboundFile> handed = [];
        _work = async ct =>
        {
            await WriteAsync("/outbox/a.txt");
            answer = await AttachAsAsync("/outbox", ["/outbox/a.txt"], ct);
            return "done";
        };

        await _host.Services.GetRequiredService<ICrewRunner>().RunAsync("support", "go", "test", conversation: new HostedConversation
        {
            Channel = Alice.Channel,
            ConversationId = Alice.ConversationId,
            PrepareAsync = _ => Task.FromResult(new PreparedRun("go")),
            SendFilesAsync = async (files, ct) =>
            {
                handed = files;
                var resolved = AgentFileSystemScope.Resolve("other", [new FileSystemScopeEntry("/output/closed")], Fs.GetAvailableMounts());
                Task<Stream> opening;
                using (ExecutionContext.SuppressFlow())
                {
                    opening = Task.Run(
                        async () =>
                        {
                            // Another agent's scope, which leaves the file out: the one that attached it is what counts.
                            seen = AgentRunScope.Current is null ? "outside" : "inside";
                            await using var other = AgentRunScope.Create("2", "other", null, resolved);
                            using var entered = other.Enter();
                            using var call = AgentRunScope.EnterToolCall();
                            return await files[0].OpenAsync(ct);
                        },
                        ct);
                }

                await using var stream = await opening;
                using var reader = new StreamReader(stream);
                Assert.Equal("text of /outbox/a.txt", await reader.ReadToEndAsync(ct));
                return [new FileSendResult("a.txt", FileSendOutcome.Sent)];
            },
        });

        Assert.Single(handed);
        Assert.Equal("outside", seen);
        Assert.True(answer!.Success, answer.Error);
    }

    /// <summary>Another text of the same length, at another date.</summary>
    private async Task RewriteAsync(string path, CancellationToken ct)
    {
        var before = (await Fs.TryGetEntryAsync(path, ct))!;
        while ((await Fs.TryGetEntryAsync(path, ct))!.LastModified == before.LastModified)
        {
            await Task.Delay(Polling.DefaultInterval, ct);
            await Fs.WriteAllTextAsync(path, new string('x', (int)before.SizeBytes), ct);
        }

        Assert.Equal(before.SizeBytes, (await Fs.TryGetEntryAsync(path, ct))!.SizeBytes);
    }

    /// <summary>The run is over when what it offered is accepted.</summary>
    private async Task<OutboundFile> OfferedAsync(string path)
    {
        OutboundFile? offered = null;
        _sender.Result = f =>
        {
            offered = f;
            return new FileSendResult(f.Name, FileSendOutcome.AwaitingConsent);
        };
        await RunAsync(async ct =>
        {
            await WriteAsync(path);
            await AttachAsync(ct, path);
            return "done";
        });
        return offered!;
    }

    [Theory]
    [InlineData("/outbox/a.txt")]
    [InlineData("/output/open/a.txt")]
    public async Task ShouldOpenWhatItOffered_AfterTheRun(string path)
    {
        Build();
        var offered = await OfferedAsync(path);

        await using var stream = await offered.OpenAsync(Ct);
        using var reader = new StreamReader(stream);

        Assert.Equal("text of " + path, await reader.ReadToEndAsync(Ct));
    }

    [Fact]
    public async Task ShouldNotSendWhatWasNotOffered()
    {
        Build();
        var offered = await OfferedAsync("/outbox/a.txt");
        _sender.Result = f => new FileSendResult(f.Name, FileSendOutcome.Sent);

        // Another run of the conversation rewrites it, to the same length.
        await RunAsync(async ct =>
        {
            await RewriteAsync("/outbox/a.txt", ct);
            return "done";
        });

        await Assert.ThrowsAsync<IOException>(() => offered.OpenAsync(Ct));
    }

    [Fact]
    public async Task ShouldNotSendWhatShrankSince()
    {
        Build();
        var offered = await OfferedAsync("/output/open/a.txt");
        var file = Path.Combine(Output, "open", "a.txt");
        var date = File.GetLastWriteTimeUtc(file);
        await File.WriteAllTextAsync(file, "less", Ct);
        File.SetLastWriteTimeUtc(file, date);

        await Assert.ThrowsAsync<IOException>(() => offered.OpenAsync(Ct));
    }

    [Fact]
    public async Task ShouldNotSendWhatTheCrewNoLongerOpens()
    {
        Build();
        var offered = await OfferedAsync("/output/open/a.txt");
        var crew = await File.ReadAllTextAsync(_harness.CrewPath, Ct);
        await File.WriteAllTextAsync(_harness.CrewPath, crew.Replace(Declared, string.Empty, StringComparison.Ordinal), Ct);

        Assert.Equal(DownloadableFiles.NoFile, (await Assert.ThrowsAsync<FileNotFoundException>(() => offered.OpenAsync(Ct))).Message);
    }

    // What an offer keeps, it keeps for an hour.
    [Fact]
    public async Task ShouldKeepNothingOfTheRunThatOffered()
    {
        Build();
        var offered = await OfferedAsync("/outbox/a.txt");

        var kept = Reached(offered.OpenAsync.Target!);

        Assert.DoesNotContain(kept, o => o is ExecutionContext or AgentRunScope or IConversationAttachments);
    }

    /// <summary>What the closure of a delegate holds, and the closures it holds.</summary>
    private static List<object> Reached(object closure)
    {
        var held = new List<object>();
        foreach (var field in closure.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
        {
            if (field.GetValue(closure) is not { } value || value.GetType().IsPrimitive || ReferenceEquals(value, closure))
                continue;
            held.Add(value);
            if (value is Delegate { Target: { } target })
                held.AddRange(Reached(target));
            else if (value.GetType().Name.StartsWith("<>c", StringComparison.Ordinal))
                held.AddRange(Reached(value));
        }

        return held;
    }

    // ----- how many -----

    [Fact]
    public async Task ShouldCapTheFilesOfARun()
    {
        Build(s => s["MaxAttachedFilesPerRun"] = 2);
        ToolCallResponse? first = null;
        ToolCallResponse? second = null;

        await RunAsync(async ct =>
        {
            await WriteAsync("/outbox/a.txt", "/outbox/b.txt", "/outbox/c.txt");
            first = await AttachAsync(ct, "/outbox/a.txt", "/outbox/a.txt", "/outbox/b.txt", "/outbox/c.txt");
            second = await AttachAsync(ct, "/outbox/c.txt");
            return "done";
        });

        Assert.Equal(["a.txt=text of /outbox/a.txt", "b.txt=text of /outbox/b.txt"], Assert.Single(_sender.Messages));
        Assert.True(first!.Success, first.Error);
        Assert.Contains("MaxAttachedFilesPerRun", Shown(first), StringComparison.Ordinal);
        Assert.Contains("MaxAttachedFilesPerRun", second!.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldCapTheMessagesOfARun()
    {
        Build(s => s["MaxAttachedMessagesPerRun"] = 2);
        ToolCallResponse? last = null;

        await RunAsync(async ct =>
        {
            await WriteAsync("/outbox/a.txt");
            for (var i = 0; i < 5; i++)
                last = await AttachAsync(ct, "/outbox/a.txt");
            return "done";
        });

        Assert.Equal(2, _sender.Messages.Count);
        Assert.Contains("MaxAttachedMessagesPerRun", last!.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldHandOverNoMoreThanOneMessageAtATime()
    {
        Build(s => s["MaxAttachedFilesPerRun"] = 25);
        ToolCallResponse? answer = null;

        await RunAsync(async ct =>
        {
            var paths = Enumerable.Range(10, 25).Select(i => $"/outbox/{i}.txt").ToArray();
            await WriteAsync(paths);
            answer = await AttachAsync(ct, paths);
            return "done";
        });

        Assert.True(answer!.Success, answer.Error);
        Assert.Equal([10, 10, 5], _sender.Messages.Select(m => m.Length));
    }

    [Fact]
    public async Task ShouldHandOverNoMoreBytesThanOneRequestTakes()
    {
        Build();
        ToolCallResponse? answer = null;

        await RunAsync(async ct =>
        {
            foreach (var name in (string[])["a.bin", "b.bin"])
                await File.WriteAllBytesAsync(Path.Combine(Output, "open", name), new byte[(DiscordFiles.RequestBytes / 2) + 1], ct);
            answer = await AttachAsync(ct, "/output/open/a.bin", "/output/open/b.bin");
            return "done";
        });

        Assert.True(answer!.Success, answer.Error);
        Assert.Equal(2, _sender.Messages.Count);
        Assert.All(_sender.Messages, m => Assert.Single(m));
    }

    [Fact]
    public async Task ShouldCapTheBytesOfARun()
    {
        Build(s => s["MaxAttachedBytesPerRun"] = 30);
        ToolCallResponse? answer = null;

        await RunAsync(async ct =>
        {
            await WriteAsync("/outbox/a.txt", "/outbox/b.txt");
            answer = await AttachAsync(ct, "/outbox/a.txt", "/outbox/b.txt");
            return "done";
        });

        Assert.Equal("a.txt=text of /outbox/a.txt", Assert.Single(Assert.Single(_sender.Messages)));
        Assert.Contains("too_large", Shown(answer!), StringComparison.Ordinal);
        Assert.Contains("MaxAttachedBytesPerRun", Shown(answer!), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldNotCountWhatTheChannelFindsTooLarge()
    {
        Build(s => s["MaxAttachedMessagesPerRun"] = 1);
        ToolCallResponse? large = null;
        ToolCallResponse? then = null;

        await RunAsync(async ct =>
        {
            await WriteAsync("/outbox/a.txt");
            _sender.Result = f => new FileSendResult(f.Name, FileSendOutcome.TooLarge, "it is too large");
            large = await AttachAsync(ct, "/outbox/a.txt");
            _sender.Result = f => new FileSendResult(f.Name, FileSendOutcome.Sent);
            then = await AttachAsync(ct, "/outbox/a.txt");
            return "done";
        });

        Assert.False(large!.Success);
        Assert.Contains("it is too large", large.Error, StringComparison.Ordinal);
        Assert.True(then!.Success, then.Error);
    }

    [Fact]
    public async Task ShouldNotPassTheCap_WhenAnAgentCallsItInParallel()
    {
        Build(s => s["MaxAttachedFilesPerRun"] = 3);

        await RunAsync(async ct =>
        {
            await WriteAsync("/outbox/a.txt");
            await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => AttachAsync(ct, "/outbox/a.txt"), ct)));
            return "done";
        });

        Assert.Equal(3, _sender.Messages.Sum(m => m.Length));
    }

    // ----- what the channel answers, and the end -----

    [Fact]
    public async Task ShouldSayThatThePersonMustAccept()
    {
        Build();
        _sender.Result = f => new FileSendResult(f.Name, FileSendOutcome.AwaitingConsent);
        ToolCallResponse? answer = null;

        await RunAsync(async ct =>
        {
            await WriteAsync("/outbox/a.txt");
            answer = await AttachAsync(ct, "/outbox/a.txt");
            return "done";
        });

        Assert.True(answer!.Success, answer.Error);
        Assert.Contains("awaiting_consent", Shown(answer), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldSayThatTheChannelSendsNoFile()
    {
        Build();
        ToolCallResponse? answer = null;
        _work = async ct =>
        {
            await WriteAsync("/outbox/a.txt");
            answer = await AttachAsync(ct, "/outbox/a.txt");
            return "done";
        };

        // What TeamsResponder answers.
        await _host.Services.GetRequiredService<ICrewRunner>().RunAsync("support", "go", "test", conversation: new HostedConversation
        {
            Channel = "teams",
            ConversationId = "19:a",
            PrepareAsync = _ => Task.FromResult(new PreparedRun("go")),
            SendFilesAsync = (files, _) => FileSendResult.NoneSent(files),
        });

        Assert.False(answer!.Success);
        Assert.Contains("does not send files", answer.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldRefuseTheFile_WhenTheChannelFails()
    {
        Build();
        _sender.Throws = new InvalidOperationException("the secret endpoint");
        ToolCallResponse? answer = null;

        var result = await RunAsync(async ct =>
        {
            await WriteAsync("/outbox/a.txt");
            answer = await AttachAsync(ct, "/outbox/a.txt");
            return "done";
        });

        Assert.True(result.Succeeded);
        Assert.False(answer!.Success);
        Assert.Contains("did not take it", answer.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", answer.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldEndTheSending_WhenTheRunIsStopped()
    {
        Build();
        var id = string.Empty;
        var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _sender.Before = async ct =>
        {
            sending.SetResult();
            await Task.Delay(Polling.DefaultTimeout, ct);
        };
        ToolCallResponse? answer = null;
        _work = async ct =>
        {
            await WriteAsync("/outbox/a.txt");
            // A tool call whose own token nobody cancels.
            answer = await AttachAsync(CancellationToken.None, "/outbox/a.txt");
            ct.ThrowIfCancellationRequested();
            return "done";
        };

        var run = RunAsync(Alice, _sender, started =>
        {
            id = started;
            return Task.CompletedTask;
        });
        await sending.Task.WaitAsync(Polling.DefaultTimeout, Ct);
        Assert.True(_host.Services.GetRequiredService<CrewHostRegistry>().RequestStop(id));
        var result = await run.WaitAsync(Polling.DefaultTimeout, Ct);

        Assert.Equal(HostedRunOutcome.Cancelled, result.Outcome);
        Assert.False(answer!.Success);
        Assert.Empty(_sender.Messages);
    }

    [Fact]
    public async Task ShouldSendNothingMore_WhenTheRunIsStoppedBetweenTwoCalls()
    {
        Build();
        var id = string.Empty;
        ToolCallResponse? answer = null;
        _work = async _ =>
        {
            await WriteAsync("/outbox/a.txt");
            _host.Services.GetRequiredService<CrewHostRegistry>().RequestStop(id);
            answer = await AttachAsync(CancellationToken.None, "/outbox/a.txt");
            return "done";
        };

        await RunAsync(Alice, _sender, started =>
        {
            id = started;
            return Task.CompletedTask;
        });

        Assert.False(answer!.Success);
        Assert.Empty(_sender.Messages);
    }

    [Fact]
    public async Task ShouldStopTheSending_WhenTheRunEnds()
    {
        Build();
        var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _sender.Before = async ct =>
        {
            await using var registration = ct.Register(stopped.SetResult);
            sending.SetResult();
            await stopped.Task.WaitAsync(Polling.DefaultTimeout, CancellationToken.None);
            ct.ThrowIfCancellationRequested();
        };
        Task<ToolCallResponse>? left = null;

        await RunAsync(async ct =>
        {
            await WriteAsync("/outbox/a.txt");
            left = Task.Run(() => AttachAsync(CancellationToken.None, "/outbox/a.txt"), CancellationToken.None);
            await sending.Task.WaitAsync(Polling.DefaultTimeout, ct);
            return "done";
        });

        await stopped.Task.WaitAsync(Polling.DefaultTimeout, Ct);
        Assert.False((await left!.WaitAsync(Polling.DefaultTimeout, Ct)).Success);
        Assert.Empty(_sender.Messages);
    }

    [Fact]
    public async Task ShouldInheritNoConversation_WhenARunIsStartedInsideAnother()
    {
        Build();
        ToolCallResponse? inner = null;
        var started = false;

        await RunAsync(async ct =>
        {
            if (started)
            {
                inner = await AttachAsync(ct, "/outbox/a.txt");
                return "inner";
            }

            started = true;
            await WriteAsync("/outbox/a.txt");
            await _host.Services.GetRequiredService<ICrewRunner>().RunAsync("support", "go", "api");
            Assert.True((await AttachAsync(ct, "/outbox/a.txt")).Success);
            return "done";
        });

        Assert.Equal(ChatAttachTool.NoConversation, inner!.Error);
        Assert.Single(_sender.Messages);
    }

    // ----- the gateway, and where the tool is -----

    [Fact]
    public async Task ShouldReachTheThreadTheMessageCameFrom()
    {
        Build();
        var responder = new RecordingResponder();
        _work = async ct =>
        {
            await WriteAsync("/outbox/a.txt");
            await AttachAsync(ct, "/outbox/a.txt");
            return "done";
        };
        var gateway = new ChatGateway(
            _host.Services.GetRequiredService<ICrewRunner>(), new ThreadIsRunRouter("support"), new AllowListChatAuthorizer(["alice"]),
            _host.Services.GetRequiredService<CrewHostRegistry>(), NullLogger<ChatGateway>.Instance, _host.Services.GetRequiredService<ConversationFiles>());

        await gateway.HandleAsync(
            new InboundMessage { Channel = Alice.Channel, ConversationId = Alice.ConversationId, SenderId = "alice", Text = "go", ReceivedAt = DateTimeOffset.UnixEpoch },
            responder,
            Ct);

        Assert.Equal("a.txt", Assert.Single(responder.Files).Name);
    }

    [Fact]
    public async Task ShouldBeATool_WhetherTheHostKeepsFilesOrNot()
    {
        Build(enabled: false);

        var tool = await _host.Services.GetRequiredService<IToolRegistry>().GetToolByNameAsync("chat_attach", Ct);

        Assert.NotNull(tool);
        Assert.Equal(ToolAccess.Execute, tool.Access);
        Assert.Contains(_host.Services.GetServices<IBaseTool>(), t => ToolContracts.StaysWithCaller(t) && t.Name == "chat_attach");
    }

    [Theory]
    [InlineData("MaxAttachedFilesPerRun")]
    [InlineData("MaxAttachedMessagesPerRun")]
    [InlineData("MaxAttachedBytesPerRun")]
    public void ShouldRefuseTheStart_WhenALimitIsNotPositive(string key)
    {
        var options = key switch
        {
            "MaxAttachedFilesPerRun" => new HostFilesOptions { MaxAttachedFilesPerRun = 0 },
            "MaxAttachedMessagesPerRun" => new HostFilesOptions { MaxAttachedMessagesPerRun = 0 },
            _ => new HostFilesOptions { MaxAttachedBytesPerRun = 0 },
        };

        Assert.Contains(key, options.Problem(), StringComparison.Ordinal);
    }
}
