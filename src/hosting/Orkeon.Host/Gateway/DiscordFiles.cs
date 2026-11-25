using System.Globalization;
using System.Net;
using Discord;
using Discord.Net;

namespace Orkeon.Host.Gateway;

/// <summary>
/// Sends files in as many Discord messages as they need, bounded in number and in bytes. A file is opened when its message
/// leaves, not before, and closed once it has left.
/// </summary>
internal static class DiscordFiles
{
    /// <summary>The files Discord takes in one message.</summary>
    public const int MaxPerMessage = 10;

    /// <summary>The bytes of files in one request: Discord's 25 MiB, less what the rest of the request takes.</summary>
    public const long RequestBytes = 24L * 1024 * 1024;

    /// <summary>What is held to when Discord announces no limit: the smallest it has had for a server.</summary>
    public const long UnknownLimit = 10L * 1024 * 1024;

    /// <summary>An upload takes longer than the library gives a request; one that timed out may have arrived, and is not sent twice.</summary>
    public static RequestOptions Upload(CancellationToken ct) => new() { Timeout = 120_000, CancelToken = ct, RetryMode = RetryMode.RetryRatelimit };

    /// <summary>The limit of one file, of what Discord announces.</summary>
    public static long LimitOf(ulong announced) => announced == 0 ? UnknownLimit : (long)Math.Min(announced, (ulong)RequestBytes);

    /// <summary>Sends the files; the answer names each one, in their order.</summary>
    /// <param name="files">The files.</param>
    /// <param name="limit">The size Discord takes of one file here.</param>
    /// <param name="post">Posts one message; the streams last as long as it.</param>
    /// <param name="ct">Stops the sending.</param>
    /// <param name="failed">Told of each failure, for the log.</param>
    public static async Task<IReadOnlyList<FileSendResult>> SendAsync(
        IReadOnlyList<OutboundFile> files,
        long limit,
        Func<IReadOnlyList<FileAttachment>, Task> post,
        CancellationToken ct,
        Action<Exception>? failed = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(post);
        var most = Math.Min(limit, RequestBytes);
        var results = new FileSendResult[files.Count];
        var batch = new List<int>();
        long bytes = 0;
        for (var i = 0; i < files.Count; i++)
        {
            if (files[i].Size > most)
            {
                results[i] = TooLarge(files[i], most);
                continue;
            }

            if (batch.Count == MaxPerMessage || (batch.Count > 0 && bytes + files[i].Size > RequestBytes))
            {
                await SendBatchAsync(files, batch, most, post, results, failed, ct).ConfigureAwait(false);
                batch.Clear();
                bytes = 0;
            }

            batch.Add(i);
            bytes += files[i].Size;
        }

        if (batch.Count > 0)
            await SendBatchAsync(files, batch, most, post, results, failed, ct).ConfigureAwait(false);
        return results;
    }

    private static async Task SendBatchAsync(
        IReadOnlyList<OutboundFile> files,
        List<int> batch,
        long most,
        Func<IReadOnlyList<FileAttachment>, Task> post,
        FileSendResult[] results,
        Action<Exception>? failed,
        CancellationToken ct)
    {
        var opened = new List<(int Index, Stream Stream)>();
        try
        {
            foreach (var i in batch)
            {
                try
                {
                    opened.Add((i, await files[i].OpenAsync(ct).ConfigureAwait(false)));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
#pragma warning disable CA1031 // A file that cannot be opened must not keep the others.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    failed?.Invoke(ex);
                    results[i] = new FileSendResult(files[i].Name, FileSendOutcome.Refused, "it is being written, or is gone: try again");
                }
            }

            if (opened.Count == 0)
                return;

            FileSendResult Said(int i, Exception? ex) => ex switch
            {
                null => new FileSendResult(files[i].Name, FileSendOutcome.Sent),
                HttpException { HttpCode: HttpStatusCode.RequestEntityTooLarge } => TooLarge(files[i], most),
                HttpException { HttpCode: HttpStatusCode.Forbidden } => new FileSendResult(files[i].Name, FileSendOutcome.Refused, "the bot may not attach files here"),
                TimeoutException or OperationCanceledException => new FileSendResult(files[i].Name, FileSendOutcome.Refused, "it took too long to send, and may have arrived"),
                _ => new FileSendResult(files[i].Name, FileSendOutcome.Refused, "Discord did not take it"),
            };
            Exception? refusal = null;
            try
            {
                await post([.. opened.Select(o => new FileAttachment(o.Stream, files[o.Index].Name))]).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // The channel answers for each file; what it could not send is said, not thrown.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                failed?.Invoke(ex);
                refusal = ex;
            }

            foreach (var (index, _) in opened)
                results[index] = Said(index, refusal);
        }
        finally
        {
            foreach (var (_, stream) in opened)
                await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static FileSendResult TooLarge(OutboundFile file, long most) =>
        new(file.Name, FileSendOutcome.TooLarge, string.Create(CultureInfo.InvariantCulture, $"it is {file.Size} bytes, and Discord takes {most} bytes here"));
}
