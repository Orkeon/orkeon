using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace Orkeon.Host.Gateway;

/// <summary>
/// Sends files in a personal chat: a consent card for each, and, once accepted, the file put at the address Teams gives,
/// which is checked like the address of a received file, then shown by a card.
/// </summary>
internal sealed partial class TeamsFileExchange : IDisposable
{
    /// <summary>The largest file offered: the channel's own bound, Microsoft's page states none.</summary>
    internal const long MaxFileBytes = 25L * 1024 * 1024;

    internal const string PersonalOnly = "In Teams, files are exchanged in the personal chat with the bot only.";

    private const string ConsentCard = "application/vnd.microsoft.teams.card.file.consent";
    private const string InfoCard = "application/vnd.microsoft.teams.card.file.info";
    private const string Gone =
        "This file no longer waits for an answer here: it was answered, an hour passed, it was offered to someone else, or the host restarted. Ask for it again.";

    private readonly TeamsConsents _consents;
    private readonly TeamsFileAccess _access;
    private readonly HttpClient _http;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _uploads = new(2, 2);

    /// <param name="consents">The files waiting for an answer.</param>
    /// <param name="access">The rule of the tenant's files, and their token.</param>
    /// <param name="http">The client of the uploads: no redirection, no trace.</param>
    /// <param name="logger">Where failures are logged, without an address.</param>
    public TeamsFileExchange(TeamsConsents consents, TeamsFileAccess access, HttpClient http, ILogger logger)
    {
        _consents = consents ?? throw new ArgumentNullException(nameof(consents));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Offers each file by a consent card.</summary>
    public async Task<IReadOnlyList<FileSendResult>> OfferAsync(
        TeamsResponder responder, TeamsInboundMessage message, IReadOnlyList<OutboundFile> files, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(responder);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(files);
        var results = new List<FileSendResult>(files.Count);
        foreach (var file in files)
            results.Add(await OfferAsync(responder, message, file, ct).ConfigureAwait(false));
        return results;
    }

    private async Task<FileSendResult> OfferAsync(TeamsResponder responder, TeamsInboundMessage message, OutboundFile file, CancellationToken ct)
    {
        if (message.NoFilesHere is { } elsewhere)
            return Refused(elsewhere);
        if (_access.Unavailable is { } unavailable)
            return Refused(unavailable);
        if (file.Size <= 0)
            return Refused("Teams takes no empty file");
        if (file.Size > MaxFileBytes)
            return new FileSendResult(file.Name, FileSendOutcome.TooLarge, FormattableString.Invariant($"larger than the {MaxFileBytes / (1024 * 1024)} MiB this channel sends"));
        if (_consents.TryAdd(message.SenderId, message.ReplyTo, file) is not { } id)
            return Refused(FormattableString.Invariant($"{TeamsConsents.PerConversation} files already wait to be accepted here, or too many on this host"));

        var shown = false;
        try
        {
            shown = await responder.PostCardAsync(
                message.ReplyTo,
                new Dictionary<string, object?>
                {
                    ["contentType"] = ConsentCard,
                    ["name"] = file.Name,
                    ["content"] = new
                    {
                        description = "Accept within an hour; the offer is lost if the host restarts.",
                        sizeInBytes = file.Size,
                        acceptContext = new { fileId = id },
                        declineContext = new { fileId = id },
                    },
                },
                ct).ConfigureAwait(false);
        }
        finally
        {
            if (!shown)
                _consents.Drop(id);
        }

        return shown ? new FileSendResult(file.Name, FileSendOutcome.AwaitingConsent) : Refused("Teams refused the request to accept it");

        FileSendResult Refused(string reason) => new(file.Name, FileSendOutcome.Refused, reason);
    }

    /// <summary>Acts on an answer whose sender is on the allow list: sends the file it accepts, if it was offered to them here.</summary>
    /// <param name="responder">What answers.</param>
    /// <param name="answer">The answer, unchecked.</param>
    /// <param name="senderId">Who answered, as the validated activity says.</param>
    /// <param name="from">Where the answer came from.</param>
    /// <param name="ct">Cancels the upload.</param>
    public async Task AnswerAsync(TeamsResponder responder, TeamsConsentAnswer answer, string senderId, TeamsReplyTarget from, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(responder);
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(from);
        var accepted = answer.Action == "accept";
        if (!accepted && answer.Action != "decline")
            return;

        if (_consents.Take(answer.FileId, senderId, from.ConversationId) is not { } pending)
        {
            await responder.PostTextAsync(from, Gone, ct).ConfigureAwait(false);
            return;
        }

        var name = FileNotices.Path(pending.File.Name);
        if (!accepted)
        {
            await responder.PostTextAsync(pending.ReplyTo, $"{name} was declined.", ct).ConfigureAwait(false);
            return;
        }

        var failure = await UploadAsync(answer, pending.File, ct).ConfigureAwait(false);
        if (failure is not null)
        {
            await responder.PostTextAsync(pending.ReplyTo, $"{name} was not sent: {failure}. Ask for it again.", ct).ConfigureAwait(false);
            return;
        }

        var shown = _access.Address(new TeamsFileAddress(answer.ContentUrl, answer.Upload.TenantId, picture: false)) is not null
            && answer.UniqueId is { Length: > 0 }
            && await responder.PostCardAsync(
                pending.ReplyTo,
                new Dictionary<string, object?>
                {
                    ["contentType"] = InfoCard,
                    ["contentUrl"] = answer.ContentUrl,
                    ["name"] = pending.File.Name,
                    ["content"] = new { uniqueId = answer.UniqueId, fileType = FileType(answer, pending.File.Name) },
                },
                ct).ConfigureAwait(false);
        if (!shown)
            await responder.PostTextAsync(pending.ReplyTo, $"{name} is in your OneDrive.", ct).ConfigureAwait(false);
    }

    private static string FileType(TeamsConsentAnswer answer, string name) =>
        answer.FileType is { Length: > 0 } type ? type : Path.GetExtension(name).TrimStart('.');

    /// <summary>Puts the file where Teams says; returns why it could not, null when it did.</summary>
    private async Task<string?> UploadAsync(TeamsConsentAnswer answer, OutboundFile file, CancellationToken ct)
    {
        if (_access.Address(answer.Upload) is not { } address)
        {
            LogForeignUpload();
            return TeamsFileAccess.ForeignAddress;
        }

        await _uploads.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var content = await ReadAsync(file, ct).ConfigureAwait(false);
            if (content is null)
                return "it can no longer be read";
            if (content.Length is 0 or > MaxFileBytes)
                return "its size changed since";

            var (header, refusal) = await _access.AuthorizeAsync(address, ct).ConfigureAwait(false);
            return refusal ?? await PutAsync(address, header, content, ct).ConfigureAwait(false);
        }
        finally
        {
            _uploads.Release();
        }
    }

    private async Task<string?> PutAsync(Uri address, AuthenticationHeaderValue? header, MemoryStream content, CancellationToken ct)
    {
        var length = content.Length;
        content.Position = 0;
        using var request = new HttpRequestMessage(HttpMethod.Put, address);
        request.Headers.Authorization = header;
        request.Content = new StreamContent(content);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentLength = length;
        request.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, length - 1, length);
        try
        {
            // The headers alone: nothing of the answer is read.
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created)
                return null;
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                await _access.ForgetAsync(address.Host, ct).ConfigureAwait(false);

            LogUploadRefused((int)response.StatusCode);
            return FormattableString.Invariant($"SharePoint answered {(int)response.StatusCode}");
        }
        catch (HttpRequestException ex)
        {
            // Not the exception itself: its message may hold the address.
            LogUploadFailed(ex.HttpRequestError.ToString());
            return "SharePoint could not be reached";
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            LogUploadFailed("timeout");
            return "SharePoint did not answer in time";
        }
    }

    /// <summary>The file as it is now, read to one byte past the bound; null when it cannot be read.</summary>
    private async Task<MemoryStream?> ReadAsync(OutboundFile file, CancellationToken ct)
    {
        var content = new MemoryStream();
        try
        {
            var source = await file.OpenAsync(ct).ConfigureAwait(false);
            await using var lease = source.ConfigureAwait(false);
            var block = new byte[81920];
            int read;
            while (content.Length <= MaxFileBytes && (read = await source.ReadAsync(block, ct).ConfigureAwait(false)) > 0)
                await content.WriteAsync(block.AsMemory(0, read), ct).ConfigureAwait(false);
            return content;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await content.DisposeAsync().ConfigureAwait(false);
            throw;
        }
#pragma warning disable CA1031 // A file that cannot be read is not sent, and its exception may name a path.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            await content.DisposeAsync().ConfigureAwait(false);
            LogUnreadable(ex.GetType().Name);
            return null;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _uploads.Dispose();

    [LoggerMessage(Level = LogLevel.Warning, Message = "A Teams upload was refused: its address is not on the SharePoint of the configured tenant (Teams:SharePointNames)")]
    private partial void LogForeignUpload();

    [LoggerMessage(Level = LogLevel.Warning, Message = "SharePoint refused a Teams upload: HTTP {Status}")]
    private partial void LogUploadRefused(int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A Teams upload failed: {Reason}")]
    private partial void LogUploadFailed(string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A file accepted in Teams could not be read ({Error})")]
    private partial void LogUnreadable(string error);
}
