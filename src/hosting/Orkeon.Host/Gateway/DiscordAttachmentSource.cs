using System.Net;

namespace Orkeon.Host.Gateway;

/// <summary>
/// Reads the files of one Discord message from Discord's file server, and from nowhere else: HTTPS, its host, no
/// redirection, no credential. The addresses are signed and expire: they live here as long as the message, and no
/// exception carries one.
/// </summary>
/// <param name="http">The client, over <see cref="NewHandler"/>.</param>
/// <param name="addresses">The address of each file, by identifier.</param>
/// <param name="idle">How long a file may send nothing before it is given up; 30 seconds by default.</param>
internal sealed class DiscordAttachmentSource(HttpClient http, IReadOnlyDictionary<string, string> addresses, TimeSpan? idle = null) : IAttachmentSource
{
    private const string FileHost = "cdn.discordapp.com";
    private readonly TimeSpan _idle = idle ?? TimeSpan.FromSeconds(30);

    /// <summary>
    /// The handler of the client. No diagnostic activity: a trace of the request would carry the address.
    /// </summary>
    public static SocketsHttpHandler NewHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        ActivityHeadersPropagator = null,
        ConnectTimeout = TimeSpan.FromSeconds(15),
    };

    /// <inheritdoc />
    public async Task<Stream> OpenAsync(InboundAttachment attachment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        if (!addresses.TryGetValue(attachment.Id, out var written)
            || !Uri.TryCreate(written, UriKind.Absolute, out var address)
            || address.Scheme != Uri.UriSchemeHttps
            || !address.IsDefaultPort
            || address.UserInfo.Length > 0
            || !string.Equals(address.Host, FileHost, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("The file has no address on Discord's file server.");
        }

        HttpResponseMessage? response = null;
        try
        {
            response = await http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
                throw new IOException(FormattableString.Invariant($"Discord's file server answered {(int)response.StatusCode}."));

            var content = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var stream = new IdleBoundedStream(response, content, _idle, "Discord's file server");
            response = null;
            return stream;
        }
        catch (HttpRequestException ex)
        {
            // Not the exception itself: its message may hold the address.
            throw new IOException($"Discord's file server could not be read ({ex.HttpRequestError}).");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new IOException("Discord's file server did not answer in time.");
        }
        finally
        {
            response?.Dispose();
        }
    }
}
