using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.ConsoleApp.Replay;
using Orkeon.ConsoleApp.Tests.Fakes;

namespace Orkeon.ConsoleApp.Tests.Replay;

/// <summary><c>/replay &lt;file&gt;</c>: starts an interactive replay, refused while one runs.</summary>
public sealed class ReplayCommandTests : IDisposable
{
    private readonly FakeConsoleAdapter _inner = new();
    private readonly FakeFileSystemService _fileSystem = new();
    private readonly ReplayIdleGate _gate = new(() => new CommandInstanceRegistry(), () => null);
    private readonly ReplayConsoleAdapter _replay;
    private readonly ReplayCommand _command;

    public ReplayCommandTests()
    {
        _replay = new ReplayConsoleAdapter(_inner, _gate);
        _command = new ReplayCommand(_replay, _fileSystem);
        _fileSystem.AddFile("/workspace/demo.replay", "/help\n/tasks\n");
    }

    public void Dispose() => _gate.Dispose();

    private Task<CommandResult> Run(params string[] args)
        => _command.ExecuteAsync(
            new CommandContext
            {
                RawInput = "/replay " + string.Join(' ', args),
                Args = args,
                Console = _inner,
                Scope = new ServiceCollection().BuildServiceProvider(),
            },
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task ShouldStartAnInteractiveReplay_WhenTheFileReads()
    {
        var result = await Run("/workspace/demo.replay");

        Assert.False(result.Failed);
        Assert.Equal("replaying /workspace/demo.replay: 2 line(s)", result.Message);
        Assert.True(_replay.IsActive);
    }

    [Fact]
    public async Task ShouldRefuse_WhileAReplayRuns()
    {
        await Run("/workspace/demo.replay");

        var result = await Run("/workspace/demo.replay");

        Assert.True(result.Failed);
        Assert.Equal("a replay is already running", result.Message);
    }

    [Fact]
    public async Task ShouldFailWithTheUsage_WhenNoFileIsGiven()
    {
        var result = await Run();

        Assert.True(result.Failed);
        Assert.Equal("usage: /replay <file>", result.Message);
    }

    [Fact]
    public async Task ShouldFail_WhenTheFileIsMissing()
    {
        var result = await Run("/workspace/missing.replay");

        Assert.True(result.Failed);
        Assert.Equal("replay file not found: /workspace/missing.replay", result.Message);
        Assert.False(_replay.IsActive);
    }
}
