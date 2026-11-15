using Microsoft.Extensions.AI;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>Chat client that makes tool calls for a number of turns, then answers.</summary>
public sealed class StubToolThenAnswerChatClient(int toolTurns, string answer) : IChatClient
{
    private int _calls;

    public System.Threading.Tasks.Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var turn = _calls++;
        var message = turn < toolTurns
            ? new ChatMessage(ChatRole.Assistant, [new FunctionCallContent($"c{turn}", "busy", arguments: null)])
            : new ChatMessage(ChatRole.Assistant, answer);
        return System.Threading.Tasks.Task.FromResult(new ChatResponse([message]));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
