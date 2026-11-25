using Orkeon.Host.Gateway;

namespace Orkeon.Host.Tests.Doubles;

/// <summary>A channel whose messages the test hands over itself.</summary>
internal sealed class FakeChatChannel : IChatChannel, IAsyncDisposable
{
    private readonly TaskCompletionSource<Func<InboundMessage, CancellationToken, Task>> _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Name => "discord";

    public RecordingResponder Recorded { get; } = new();

    public IChatResponder Responder => Recorded;

    public event Func<CommandInvocation, Task<CommandReply>>? CommandInvoked;

    /// <summary>What the service does with a message, once it listens.</summary>
    public Task<Func<InboundMessage, CancellationToken, Task>> Listening => _listening.Task;

    public Task<CommandReply> InvokeAsync(CommandInvocation invocation) =>
        CommandInvoked?.Invoke(invocation) ?? throw new InvalidOperationException("Nobody listens to the commands.");

    public Task RunAsync(Func<InboundMessage, CancellationToken, Task> onMessage, CancellationToken ct)
    {
        _listening.TrySetResult(onMessage);
        return Task.Delay(Timeout.Infinite, ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
