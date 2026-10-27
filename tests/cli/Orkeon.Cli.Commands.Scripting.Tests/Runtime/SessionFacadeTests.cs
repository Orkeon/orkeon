using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Cli.Commands.Scripting.Loading;
using Orkeon.Cli.Commands.Scripting.Runtime;
using Orkeon.Cli.Commands.Scripting.Tests.Fixtures;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.Communication;
using Orkeon.Infrastructure.Session;
using Orkeon.Scripting;
using Orkeon.Scripting.Toolchain;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Cli.Commands.Scripting.Tests.Runtime;

/// <summary>
/// #227: <c>ctx.services.get("session")</c> saves the REPL's session now, to the file Persist writes at exit, and
/// loads another saved session in its place - the conversation buffer and the state bag - while the REPL runs.
/// </summary>
public sealed class SessionFacadeTests
{
    private const string Sessions = "/workspace/.orkeon/sessions";
    private const string Project = "/home/user/project";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FakeFileSystemService NewFs() =>
        new FakeFileSystemService().AddMount("/workspace", FileAccessRights.ReadWrite);

    private static FileSessionBufferService NewSession(IFileSystemService fs) =>
        new(fs, new CliSessionOptions { SessionsDirectory = Sessions, Persist = true });

    private static SessionFacade Facade(ISessionBufferService session)
    {
        using var services = new ServiceCollection().AddSingleton(session).BuildServiceProvider();
        var locator = new ScriptServiceLocator(DefaultScriptServiceWhitelist.Build().Build(), services);
        Assert.True(locator.has(ScriptServiceKeys.Session));
        return Assert.IsType<SessionFacade>(locator.get(ScriptServiceKeys.Session));
    }

    /// <summary>A session of another project saved to its own file, as Persist writes it at exit.</summary>
    private static async Task<string> SavedElsewhereAsync(IFileSystemService fs)
    {
        var other = NewSession(fs);
        await other.LoadAsync("/home/user/other", Ct);
        other.ReplaceMessages([
            new SessionMessage { Role = "user", Content = "loaded-question" },
            new SessionMessage { Role = "assistant", Content = "loaded-answer" },
        ]);
        other.SetState("loaded-key", "loaded-value");
        other.SetTitle("Loaded");
        await other.SaveAsync(Ct);
        return other.SessionFilePath!;
    }

    [Fact]
    [Trait("BR", "#227")]
    public async Task ShouldWriteTheFilePersistWritesAtExit_WhenSaveIsCalled()
    {
        var fs = NewFs();
        var session = NewSession(fs);
        await session.LoadAsync(Project, Ct);
        session.ReplaceMessages([new SessionMessage { Role = "user", Content = "saved-now" }]);
        session.SetState("k", "v");

        var written = await Facade(session).save();

        Assert.Equal(session.SessionFilePath, written);
        var reopened = NewSession(fs);
        await reopened.LoadAsync(Project, Ct);
        Assert.Equal("saved-now", Assert.Single(reopened.GetMessages()).Content);
        Assert.Equal("v", reopened.GetState("k"));
        Assert.Equal(session.GetMetadata().SessionId, reopened.GetMetadata().SessionId);
    }

    [Fact]
    [Trait("BR", "#227")]
    public async Task ShouldRefuseToSave_BeforeTheHostNamedTheProject()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Facade(NewSession(NewFs())).save());

        Assert.Contains("session.save", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "#227")]
    public async Task ShouldSwapTheBufferAndTheStateBag_WhenLoadIsCalled()
    {
        var fs = NewFs();
        var path = await SavedElsewhereAsync(fs);
        var session = NewSession(fs);
        await session.LoadAsync(Project, Ct);
        session.ReplaceMessages([new SessionMessage { Role = "user", Content = "current" }]);
        session.SetState("current-key", "x");

        var count = await Facade(session).load(path);

        Assert.Equal(2, count);
        Assert.Equal(["loaded-question", "loaded-answer"], session.GetMessages().Select(m => m.Content));
        Assert.Equal("loaded-value", session.GetState("loaded-key"));
        Assert.Null(session.GetState("current-key"));
    }

    [Fact]
    [Trait("BR", "#227")]
    public async Task ShouldReportTheErrorAndLeaveTheSession_WhenTheFileIsRefused()
    {
        var fs = NewFs();
        fs.AddFile("/workspace/notes.json", "{\"hello\":\"world\"}");
        var session = NewSession(fs);
        await session.LoadAsync(Project, Ct);
        session.ReplaceMessages([new SessionMessage { Role = "user", Content = "current" }]);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Facade(session).load("/workspace/notes.json"));

        Assert.Contains("/workspace/notes.json", error.Message, StringComparison.Ordinal);
        Assert.Equal("current", Assert.Single(session.GetMessages()).Content);
    }

    [Fact]
    [Trait("BR", "#227")]
    public void ShouldNotBeServed_WhenTheSessionIsNotPersisted()
    {
        using var inMemory = new ServiceCollection()
            .AddSingleton<ISessionBufferService>(new InMemorySessionBufferService())
            .BuildServiceProvider();
        using var empty = new ServiceCollection().BuildServiceProvider();

        Assert.False(new ScriptServiceLocator(DefaultScriptServiceWhitelist.Build().Build(), inMemory).has(ScriptServiceKeys.Session));
        Assert.False(new ScriptServiceLocator(DefaultScriptServiceWhitelist.Build().Build(), empty).has(ScriptServiceKeys.Session));
    }

    [Fact]
    [Trait("BR", "#227")]
    public async Task ShouldContinueTheLoadedConversation_AndLeaveTheRunningInstances_WhenACommandLoads()
    {
        var directory = Path.Combine(Path.GetTempPath(), "orkeon-session-cmd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "cmd"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "cmd", "resume.cmd.ts"), """
                defineCommand({
                  name: "resume",
                  description: "Loads a saved session.",
                  async handler(args, ctx) {
                    const count = await ctx.services.get("session").load(args.raw[0]);
                    ctx.writeLine("loaded:" + count);
                    return ctx.continue();
                  },
                });
                """, Ct);
            var fs = new DiskBackedFileSystemService(directory, virtualRoot: "/workspace");
            var path = await SavedElsewhereAsync(fs);
            var session = NewSession(fs);
            await session.LoadAsync(Project, Ct);
            session.ReplaceMessages([new SessionMessage { Role = "user", Content = "current" }]);

            var dispatch = new CommandDispatchService(
                new InMemoryAgentChannel(NullLogger<InMemoryAgentChannel>.Instance),
                new AgentCommandDirectory(),
                new CommandInstanceRegistry());
            var running = dispatch.Registry.Register("watch", CommandInstanceKind.Async, "agent", "watch", Guid.NewGuid());
            var before = dispatch.Registry.List().Select(v => (v.ticket, v.state)).ToList();

            using var services = new ServiceCollection().AddSingleton<ISessionBufferService>(session).BuildServiceProvider();
            var loader = new ScriptCommandLoader(
                fs,
                PassThroughTranspiler.Instance,
                new JsEngineFactory(),
                Microsoft.Extensions.Options.Options.Create(new ScriptCommandLoaderOptions { Directories = ["/workspace/cmd"], EsbuildTranspile = false }),
                new ScriptCommandLoaderDependencies
                {
                    Services = new ScriptServiceLocator(DefaultScriptServiceWhitelist.Build().Build(), services),
                    Dispatch = dispatch,
                });
            var (registry, _) = await loader.LoadAndRegisterAsync(Ct);
            var output = new ScriptedTestConsole();

            await registry.ScriptCommands.Single(c => c.Name == "resume").ExecuteAsync(
                new CommandContext { RawInput = "/resume " + path, Args = [path], Console = output, Scope = services }, Ct);

            Assert.Contains("loaded:2", output.Output, StringComparison.Ordinal);
            Assert.Equal(["loaded-question", "loaded-answer"], session.GetMessages().Select(m => m.Content));
            Assert.Equal(before, dispatch.Registry.List().Select(v => (v.ticket, v.state)).ToList());
            Assert.Same(running, dispatch.Registry.Get(running.Ticket));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { /* best-effort */ }
        }
    }
}
