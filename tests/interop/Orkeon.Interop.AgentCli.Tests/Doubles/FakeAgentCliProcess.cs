using System.Text.Json.Nodes;
using System.Threading.Channels;
using Orkeon.Interop.AgentCli.Engine;
using Orkeon.Interop.AgentCli.Process;

namespace Orkeon.Interop.AgentCli.Tests.Doubles;

/// <summary>
/// An agent CLI played by a test: <see cref="EmitAsync"/> writes the stream-json lines of appendix C to the
/// host, <see cref="Stdin"/> holds what the host wrote to the CLI, and <see cref="Connection"/> is the
/// relay's side of the bridge. Nothing is timed: a test orders the two sides by awaiting what the other wrote.
/// </summary>
internal sealed class FakeAgentCliProcess : IAgentCliProcess
{
    private readonly Channel<string> _stdout = Channel.CreateUnbounded<string>();

    public FakeAgentCliProcess(string systemPrompt, string? model, string? effort = null)
    {
        SystemPrompt = systemPrompt;
        Model = model;
        Effort = effort;
    }

    public string SystemPrompt { get; }

    public string? Model { get; }

    /// <summary>The thinking effort the process was started at; null for the CLI's own.</summary>
    public string? Effort { get; }

    public FakeBridgeConnection Connection { get; } = new();

    /// <summary>Every line the host wrote to the CLI's input.</summary>
    public Channel<string> Stdin { get; } = Channel.CreateUnbounded<string>();

    public bool Disposed { get; private set; }

    public ChannelReader<string> Incoming => _stdout.Reader;

    public Task<IAgentCliLineChannel> Bridge => Task.FromResult<IAgentCliLineChannel>(Connection);

    public string StandardErrorTail { get; set; } = "";

    public int? ExitCode { get; set; }

    public ValueTask WriteLineAsync(string line, CancellationToken cancellationToken) =>
        Stdin.Writer.WriteAsync(line, cancellationToken);

    /// <summary>Writes one stream-json line, as the CLI would.</summary>
    public ValueTask EmitAsync(JsonObject line) => _stdout.Writer.WriteAsync(line.ToJsonString());

    /// <summary>The CLI ends: its output closes.</summary>
    public void End(int exitCode)
    {
        ExitCode = exitCode;
        _stdout.Writer.TryComplete();
    }

    /// <summary>The next line the host wrote to the CLI's input, parsed.</summary>
    public async Task<JsonObject> NextInputAsync()
    {
        var line = await Stdin.Reader.ReadAsync(TestContext.Current.CancellationToken);
        return (JsonObject)JsonNode.Parse(line)!;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        _stdout.Writer.TryComplete();
        Connection.Close();
        return ValueTask.CompletedTask;
    }
}

/// <summary>Starts <see cref="FakeAgentCliProcess"/>es and keeps them for the test.</summary>
internal sealed class FakeAgentCliLauncher : IAgentCliLauncher
{
    private readonly Channel<FakeAgentCliProcess> _started = Channel.CreateUnbounded<FakeAgentCliProcess>();

    public List<FakeAgentCliProcess> Started { get; } = [];

    public IAgentCliProcess Start(IAgentCliEngine engine, string systemPrompt, string? model, string? effort)
    {
        var process = new FakeAgentCliProcess(systemPrompt, model, effort);
        lock (Started)
            Started.Add(process);
        _started.Writer.TryWrite(process);
        return process;
    }

    /// <summary>The next process started, in order.</summary>
    public async Task<FakeAgentCliProcess> NextAsync() =>
        await _started.Reader.ReadAsync(TestContext.Current.CancellationToken);
}
