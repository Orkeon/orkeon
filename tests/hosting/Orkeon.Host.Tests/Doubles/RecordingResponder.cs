namespace Orkeon.Host.Tests.Doubles;

using Orkeon.Host.Gateway;

/// <summary>Records what the gateway said, in the order it said it.</summary>
internal sealed class RecordingResponder : IChatResponder
{
    public List<(string Kind, string Text)> Sent { get; } = [];

    /// <summary>The files handed over, in order.</summary>
    public List<OutboundFile> Files { get; } = [];

    public int MaxTextLength { get; set; } = 2000;

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

    public Task<IReadOnlyList<FileSendResult>> SendFilesAsync(InboundMessage message, IReadOnlyList<OutboundFile> files, CancellationToken ct)
    {
        Files.AddRange(files);
        return Task.FromResult<IReadOnlyList<FileSendResult>>([.. files.Select(f => new FileSendResult(f.Name, FileSendOutcome.Sent))]);
    }
}
