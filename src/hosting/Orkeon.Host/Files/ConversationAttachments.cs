using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.FileSystem;
using Orkeon.Host.Gateway;
using Orkeon.Hosting.Conversations;

namespace Orkeon.Host.Files;

/// <summary>
/// What the agents of one run send to its conversation: the files <c>/get</c> would send, as many as the settings allow a
/// run, until it ends or is stopped. Every agent of the run shares the counts.
/// </summary>
internal sealed partial class ConversationAttachments : IConversationAttachments, IDisposable
{
    private readonly DownloadableFiles _downloads;
    private readonly HostFilesOptions _options;
    private readonly ConversationRef _conversation;
    private readonly HostedCrewOptions _crew;
    private readonly Func<IReadOnlyList<OutboundFile>, CancellationToken, Task<IReadOnlyList<FileSendResult>>> _send;
    private readonly string _runId;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _ended;
    private readonly CancellationToken _token;

    private readonly ConcurrentDictionary<string, (long Size, DateTimeOffset? Written)> _sent = new(StringComparer.Ordinal);
    private readonly Lock _countsLock = new();
    private int _files;
    private int _messages;
    private long _bytes;
    private volatile bool _closed;

    /// <summary>Opens the sending side of a run.</summary>
    /// <param name="downloads">Which files may be sent.</param>
    /// <param name="options">The caps of a run.</param>
    /// <param name="conversation">The conversation the run answers.</param>
    /// <param name="crew">The run's crew.</param>
    /// <param name="run">The run, whose stop and deadline stop the sending.</param>
    /// <param name="send">Sends files to the conversation.</param>
    /// <param name="logger">Receives what was sent, and the failures.</param>
    public ConversationAttachments(
        DownloadableFiles downloads,
        HostFilesOptions options,
        ConversationRef conversation,
        HostedCrewOptions crew,
        HostedRun run,
        Func<IReadOnlyList<OutboundFile>, CancellationToken, Task<IReadOnlyList<FileSendResult>>> send,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(run);
        _downloads = downloads ?? throw new ArgumentNullException(nameof(downloads));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _conversation = conversation;
        _crew = crew ?? throw new ArgumentNullException(nameof(crew));
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _runId = run.Id;
        _ended = CancellationTokenSource.CreateLinkedTokenSource(run.Cancellation.Token);
        _token = _ended.Token;
    }

    /// <inheritdoc />
    public bool IsOpen => !_closed;

    /// <summary>Whether the conversation has the file as it is: what changed since it was sent, or awaits consent, it has not.</summary>
    public bool WasSent(string path, long size, DateTimeOffset written) => _sent.TryGetValue(path, out var sent) && sent == (size, written);

    /// <summary>Ends the sending with the run: what outlives it sends nothing, and what it was sending stops.</summary>
    public void Dispose()
    {
        if (_closed)
            return;
        _closed = true;
        _ended.Cancel();
        _ended.Dispose();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AttachedFile>> AttachAsync(IReadOnlyList<string> paths, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(paths);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _token);
        var token = linked.Token;
        token.ThrowIfCancellationRequested();

        var asked = paths.Select(p => p.Trim()).Distinct(StringComparer.Ordinal).ToList();
        var results = new AttachedFile[asked.Count];
        var found = new List<(int Index, OutboundFile File)>();
        for (var i = 0; i < asked.Count; i++)
        {
            if (await FindAsync(asked[i], token).ConfigureAwait(false) is { } file)
                found.Add((i, file));
            else
                results[i] = new AttachedFile(asked[i], AttachedFileStatus.Refused, DownloadableFiles.NoFile);
        }

        foreach (var message in Admit(found, (i, status, reason) => results[i] = new AttachedFile(asked[i], status, reason)))
        {
            var sent = await SendAsync(message, token).ConfigureAwait(false);
            for (var n = 0; n < message.Count; n++)
            {
                var (index, file) = message[n];
                if (sent[n].Outcome == FileSendOutcome.Sent)
                    _sent[asked[index]] = (file.Size, file.Written);
                results[index] = new AttachedFile(asked[index], StatusOf(sent[n].Outcome), sent[n].Reason);
            }

            GiveBack(message, sent);
        }

        var count = results.Count(r => r.Status == AttachedFileStatus.Sent);
        LogAttached(_runId, count, results.Length);
        return results;
    }

    private async Task<OutboundFile?> FindAsync(string path, CancellationToken ct)
    {
        try
        {
            return await _downloads.FindAsync(_conversation, _crew, path, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileAccessDeniedException)
        {
            LogNotSent(ex, _runId);
            return null;
        }
    }

    /// <summary>
    /// Counts the files before any is sent, so calls made together do not pass a cap, and parts them into what any channel
    /// takes as one message.
    /// </summary>
    private List<List<(int Index, OutboundFile File)>> Admit(List<(int Index, OutboundFile File)> found, Action<int, string, string> refuse)
    {
        var messages = new List<List<(int Index, OutboundFile File)>>();
        long inMessage = 0;
        lock (_countsLock)
        {
            foreach (var (index, file) in found)
            {
                if (_files >= _options.MaxAttachedFilesPerRun)
                {
                    refuse(index, AttachedFileStatus.Refused, $"a run may send {_options.MaxAttachedFilesPerRun} file(s) ({HostFilesOptions.SectionName}:MaxAttachedFilesPerRun)");
                    continue;
                }

                if (file.Size > _options.MaxAttachedBytesPerRun - _bytes)
                {
                    refuse(index, AttachedFileStatus.TooLarge, $"a run may send {_options.MaxAttachedBytesPerRun} bytes ({HostFilesOptions.SectionName}:MaxAttachedBytesPerRun)");
                    continue;
                }

                if (messages.Count == 0 || messages[^1].Count == DiscordFiles.MaxPerMessage || inMessage + file.Size > DiscordFiles.RequestBytes)
                {
                    if (_messages >= _options.MaxAttachedMessagesPerRun)
                    {
                        refuse(index, AttachedFileStatus.Refused, $"a run may send files in {_options.MaxAttachedMessagesPerRun} message(s) ({HostFilesOptions.SectionName}:MaxAttachedMessagesPerRun)");
                        continue;
                    }

                    _messages++;
                    messages.Add([]);
                    inMessage = 0;
                }

                _files++;
                _bytes += file.Size;
                inMessage += file.Size;
                messages[^1].Add((index, file));
            }
        }

        return messages;
    }

    private async Task<IReadOnlyList<FileSendResult>> SendAsync(List<(int Index, OutboundFile File)> message, CancellationToken ct)
    {
        IReadOnlyList<OutboundFile> files = [.. message.Select(m => m.File)];
        try
        {
            var sent = await _send(files, ct).ConfigureAwait(false);
            if (sent.Count == files.Count)
                return sent;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A channel that fails refuses the files; the agent reads no detail of it.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogNotSent(ex, _runId);
        }

        return [.. files.Select(f => new FileSendResult(f.Name, FileSendOutcome.Refused, "the channel did not take it"))];
    }

    /// <summary>What is too large was never posted. Anything else may have been, and stays counted.</summary>
    private void GiveBack(List<(int Index, OutboundFile File)> message, IReadOnlyList<FileSendResult> sent)
    {
        lock (_countsLock)
        {
            for (var n = 0; n < message.Count; n++)
            {
                if (sent[n].Outcome != FileSendOutcome.TooLarge)
                    continue;
                _files--;
                _bytes -= message[n].File.Size;
            }

            if (sent.All(r => r.Outcome == FileSendOutcome.TooLarge))
                _messages--;
        }
    }

    private static string StatusOf(FileSendOutcome outcome) => outcome switch
    {
        FileSendOutcome.Sent => AttachedFileStatus.Sent,
        FileSendOutcome.TooLarge => AttachedFileStatus.TooLarge,
        FileSendOutcome.AwaitingConsent => AttachedFileStatus.AwaitingConsent,
        _ => AttachedFileStatus.Refused,
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Run {RunId} sent {Sent} of {Asked} file(s) to its conversation")]
    private partial void LogAttached(string runId, int sent, int asked);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A file of run {RunId} was not sent")]
    private partial void LogNotSent(Exception ex, string runId);
}
