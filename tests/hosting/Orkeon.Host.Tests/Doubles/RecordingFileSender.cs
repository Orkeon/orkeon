using Orkeon.Host.Gateway;

namespace Orkeon.Host.Tests.Doubles;

/// <summary>The sending side of one conversation: each call is a message, whose files are read before it answers.</summary>
internal sealed class RecordingFileSender
{
    /// <summary>The messages, each with its files as <c>name=content</c>.</summary>
    public List<string[]> Messages { get; } = [];

    /// <summary>What becomes of a file; sent by default.</summary>
    public Func<OutboundFile, FileSendResult> Result { get; set; } = f => new FileSendResult(f.Name, FileSendOutcome.Sent);

    /// <summary>Runs before the files are opened.</summary>
    public Func<CancellationToken, Task> Before { get; set; } = _ => Task.CompletedTask;

    /// <summary>Thrown instead of an answer.</summary>
    public Exception? Throws { get; set; }

    public async Task<IReadOnlyList<FileSendResult>> SendAsync(IReadOnlyList<OutboundFile> files, CancellationToken ct)
    {
        await Before(ct);
        if (Throws is not null)
            throw Throws;

        var results = new List<FileSendResult>();
        var message = new List<string>();
        foreach (var file in files)
        {
            var result = Result(file);
            if (result.Outcome == FileSendOutcome.Sent)
            {
                try
                {
                    await using var stream = await file.OpenAsync(ct);
                    using var reader = new StreamReader(stream);
                    message.Add($"{file.Name}={await reader.ReadToEndAsync(ct)}");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    result = new FileSendResult(file.Name, FileSendOutcome.Refused, "it is gone");
                }
            }

            results.Add(result);
        }

        lock (Messages)
            Messages.Add([.. message]);
        return results;
    }
}
