using System.Collections.Frozen;
using System.Net;
using System.Net.Http.Headers;

namespace Orkeon.Host.Gateway;

/// <summary>
/// Reads the files of one personal chat's message: from the tenant's SharePoint, or, for a picture, from the Bot Connector.
/// HTTPS, a fixed host rule, no redirection. The addresses live here as long as the message, and no exception carries one.
/// </summary>
internal sealed class TeamsAttachmentSource : IAttachmentSource
{
    private static readonly TimeSpan s_answer = TimeSpan.FromSeconds(100);
    private static readonly TimeSpan s_idle = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly TeamsFileAccess _access;
    private readonly TeamsBotToken _token;
    private readonly FrozenSet<string> _connectors;
    private readonly IReadOnlyDictionary<string, TeamsFileAddress> _addresses;

    /// <param name="http">The client, over <see cref="DiscordAttachmentSource.NewHandler"/>.</param>
    /// <param name="access">The rule of the tenant's files, and their token.</param>
    /// <param name="token">The bot's token, for a picture.</param>
    /// <param name="connectors">The hosts of the Bot Connector.</param>
    /// <param name="addresses">Where each file says it is, by identifier.</param>
    public TeamsAttachmentSource(
        HttpClient http,
        TeamsFileAccess access,
        TeamsBotToken token,
        FrozenSet<string> connectors,
        IReadOnlyDictionary<string, TeamsFileAddress> addresses)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _token = token ?? throw new ArgumentNullException(nameof(token));
        _connectors = connectors ?? throw new ArgumentNullException(nameof(connectors));
        _addresses = addresses ?? throw new ArgumentNullException(nameof(addresses));
    }

    /// <inheritdoc />
    public async Task<Stream> OpenAsync(InboundAttachment attachment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        if (!_addresses.TryGetValue(attachment.Id, out var file))
            throw new AttachmentRefusedException("it has no address");

        return file.Picture ? await PictureAsync(file, ct).ConfigureAwait(false) : await SharedAsync(file, ct).ConfigureAwait(false);
    }

    private async Task<Stream> SharedAsync(TeamsFileAddress file, CancellationToken ct)
    {
        if (_access.Unavailable is { } unavailable)
            throw new AttachmentRefusedException(unavailable);
        if (_access.Address(file) is not { } address)
            throw new AttachmentRefusedException(TeamsFileAccess.ForeignAddress);

        var (header, refusal) = await _access.AuthorizeAsync(address, ct).ConfigureAwait(false);
        if (refusal is not null)
            throw new AttachmentRefusedException(refusal);

        return await GetAsync(address, header, "SharePoint", () => _access.ForgetAsync(address.Host, ct), ct).ConfigureAwait(false);
    }

    private async Task<Stream> PictureAsync(TeamsFileAddress file, CancellationToken ct)
    {
        if (TeamsFileAccess.Address(file.Written, _connectors) is not { } address)
            throw new AttachmentRefusedException("its address is not the Bot Connector's");

        string token;
        try
        {
            token = await _token.GetAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            throw new AttachmentRefusedException("the host could not sign in to Teams");
        }

        return await GetAsync(address, new AuthenticationHeaderValue("Bearer", token), "The Bot Connector", () => _token.ForgetAsync(token, ct), ct).ConfigureAwait(false);
    }

    private async Task<Stream> GetAsync(Uri address, AuthenticationHeaderValue? header, string server, Func<Task> forget, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.Authorization = header;
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bound.CancelAfter(s_answer);
        HttpResponseMessage? response = null;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, bound.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                await forget().ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
                throw new AttachmentRefusedException(FormattableString.Invariant($"{server} answered {(int)response.StatusCode}"));

            var stream = new IdleBoundedStream(response, await response.Content.ReadAsStreamAsync(bound.Token).ConfigureAwait(false), s_idle, server);
            response = null;
            return stream;
        }
        catch (HttpRequestException ex)
        {
            // Not the exception itself: its message may hold the address.
            throw new AttachmentRefusedException($"{server} could not be read ({ex.HttpRequestError})");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AttachmentRefusedException($"{server} did not answer in time");
        }
        finally
        {
            response?.Dispose();
        }
    }
}
