using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>A chat client that streams the scripted updates of each turn, then fails when <see cref="FailureAfterUpdates"/> is set.</summary>
public sealed class FakeStreamingChatClient : IChatClient
{
    private readonly Queue<ChatResponseUpdate[]> _turns = new();

    /// <summary>The failure every turn ends with after its updates.</summary>
    public Func<Exception>? FailureAfterUpdates { get; set; }

    public void Enqueue(params ChatResponseUpdate[] updates) => _turns.Enqueue(updates);

    public System.Threading.Tasks.Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("A turn someone reads is streamed.");

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var update in _turns.Count > 0 ? _turns.Dequeue() : [new ChatResponseUpdate(ChatRole.Assistant, "default")])
        {
            await System.Threading.Tasks.Task.Yield();
            yield return update;
        }

        if (FailureAfterUpdates is { } failure)
            throw failure();
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
