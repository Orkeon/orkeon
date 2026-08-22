namespace Orkeon.Host.Tests.Doubles;

using Orkeon.Host.Gateway;

/// <summary>Records what the gateway said, in the order it said it.</summary>
internal sealed class RecordingResponder : IChatResponder
{
    public List<(string Kind, string Text)> Sent { get; } = [];

    public Task AcknowledgeAsync(InboundMessage message, string text, CancellationToken ct)
    {
        Sent.Add(("ack", text));
        return Task.CompletedTask;
    }

    public Task ProgressAsync(InboundMessage message, string text, CancellationToken ct)
    {
        Sent.Add(("progress", text));
        return Task.CompletedTask;
    }

    public Task CompleteAsync(InboundMessage message, string text, CancellationToken ct)
    {
        Sent.Add(("complete", text));
        return Task.CompletedTask;
    }
}
