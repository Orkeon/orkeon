using System.Text.Json.Nodes;
using System.Threading.Channels;
using Orkeon.Interop.AgentCli.Process;

namespace Orkeon.Interop.AgentCli.Tests.Doubles;

/// <summary>
/// The relay's side of the bridge, played by a test: <see cref="SendAsync"/> writes what the CLI would send,
/// <see cref="Written"/> holds what the host's bridge answered, in order.
/// </summary>
internal sealed class FakeBridgeConnection : IAgentCliLineChannel
{
    private readonly Channel<string> _incoming = Channel.CreateUnbounded<string>();

    /// <summary>The lines the host's bridge wrote, read by the test.</summary>
    public Channel<string> Written { get; } = Channel.CreateUnbounded<string>();

    public ChannelReader<string> Incoming => _incoming.Reader;

    public ValueTask WriteLineAsync(string line, CancellationToken cancellationToken) =>
        Written.Writer.WriteAsync(line, cancellationToken);

    /// <summary>Sends one JSON-RPC message as the CLI would.</summary>
    public ValueTask SendAsync(JsonObject message) => _incoming.Writer.WriteAsync(message.ToJsonString());

    /// <summary>Sends a <c>tools/call</c> of <paramref name="tool"/> for <paramref name="toolUseId"/> (null: no <c>_meta</c>).</summary>
    public ValueTask CallAsync(int id, string tool, string? toolUseId, JsonObject? arguments = null)
    {
        var parameters = new JsonObject { ["name"] = tool, ["arguments"] = arguments ?? [] };
        if (toolUseId is not null)
            parameters["_meta"] = new JsonObject { ["claudecode/toolUseId"] = toolUseId };
        return SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = "tools/call", ["params"] = parameters });
    }

    /// <summary>The next line the bridge wrote, parsed.</summary>
    public async Task<JsonObject> NextAsync()
    {
        var line = await Written.Reader.ReadAsync(TestContext.Current.CancellationToken);
        return (JsonObject)JsonNode.Parse(line)!;
    }

    public void Close() => _incoming.Writer.TryComplete();
}
