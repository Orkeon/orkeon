using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Orkeon.Hosting.ToolBridge;

/// <summary>Why a request to the tool bridge failed.</summary>
internal enum ToolBridgeFailure
{
    /// <summary>The bridge could not be reached, or the connection broke.</summary>
    Unreachable,

    /// <summary>The bridge refused the token.</summary>
    TokenRefused,

    /// <summary>The bridge answered with a refusal of another kind, or an answer the caller cannot read.</summary>
    Refused,
}

/// <summary>A request to the tool bridge that failed, with its reason in the operator's words.</summary>
[SuppressMessage("Design", "CA1032", Justification = "Internal to the bridge: it is always built with its kind and its reason.")]
[SuppressMessage("Design", "CA1064", Justification = "Internal to the bridge: the proxy and the start-up step turn it into a response or a RunnerSettingsException before it leaves the assembly.")]
internal sealed class ToolBridgeException : Exception
{
    /// <summary>Builds the failure.</summary>
    /// <param name="failure">Its kind.</param>
    /// <param name="message">Its reason.</param>
    /// <param name="inner">The exception behind it, when one.</param>
    public ToolBridgeException(ToolBridgeFailure failure, string message, Exception? inner = null)
        : base(message, inner) => Failure = failure;

    /// <summary>The kind of the failure.</summary>
    public ToolBridgeFailure Failure { get; }
}

/// <summary>
/// The caller's side of the wire (#445): one <see cref="HttpClient"/> to the bridge, the token on every request.
/// The connection is bounded by <see cref="ToolBridgeClientOptions.ConnectTimeout"/>; a call is not, since a tool
/// may run for minutes, and ends with the caller's cancellation.
/// </summary>
internal sealed class ToolBridgeClient : IDisposable
{
    private readonly HttpClient _http;

    /// <summary>Builds a client of the bridge at <paramref name="address"/>.</summary>
    /// <param name="address">The bridge's base address; its path is ignored, the protocol's path is used.</param>
    /// <param name="token">The token, read from the environment by the caller.</param>
    /// <param name="connectTimeout">How long a connection may take.</param>
    [SuppressMessage("Reliability", "CA2000", Justification = "The handler is owned by the HttpClient (disposeHandler: true), which Dispose disposes.")]
    public ToolBridgeClient(Uri address, string token, TimeSpan connectTimeout)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentException.ThrowIfNullOrEmpty(token);
        Address = address;
        _http = new HttpClient(new SocketsHttpHandler { ConnectTimeout = connectTimeout, UseProxy = false }, disposeHandler: true)
        {
            BaseAddress = new Uri(address, ToolBridgeProtocol.BasePath),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(ToolBridgeProtocol.BearerScheme, token);
    }

    /// <summary>The bridge's address, as the settings give it.</summary>
    public Uri Address { get; }

    /// <summary>The tools the bridge serves.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Its answer.</returns>
    public System.Threading.Tasks.Task<BridgeToolListResponse> ListToolsAsync(CancellationToken cancellationToken) =>
        SendAsync<BridgeToolListResponse>(new HttpRequestMessage(HttpMethod.Get, ToolBridgeProtocol.ToolsEndpoint), cancellationToken);

    /// <summary>Runs one call on the bridge.</summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">Aborts the request; the caller then cancels the call with <see cref="CancelAsync"/>.</param>
    /// <returns>The tool's response.</returns>
    public System.Threading.Tasks.Task<BridgeCallResponse> CallAsync(BridgeCallRequest call, CancellationToken cancellationToken) =>
        SendAsync<BridgeCallResponse>(Post(ToolBridgeProtocol.CallEndpoint, call), cancellationToken);

    /// <summary>Asks the bridge to cancel a call in flight; a failure is ignored, the call's own request is gone already.</summary>
    /// <param name="callId">The id the call was sent with.</param>
    /// <param name="cancellationToken">Bounds the request.</param>
    /// <returns>A task that completes once the bridge answered, or failed to.</returns>
    [SuppressMessage("Design", "CA1031",
        Justification = "A cancellation is best effort: whatever fails, the call it names is abandoned by the caller already.")]
    public async System.Threading.Tasks.Task CancelAsync(string callId, CancellationToken cancellationToken)
    {
        try
        {
            using var request = Post(ToolBridgeProtocol.CancelEndpoint, new BridgeCancelRequest { CallId = callId });
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best effort.
        }
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    private static HttpRequestMessage Post<T>(string endpoint, T body) => new(HttpMethod.Post, endpoint)
    {
        Content = JsonContent.Create(body, options: ToolBridgeProtocol.JsonOptions),
    };

    private async System.Threading.Tasks.Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var owned = request;
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ToolBridgeException(ToolBridgeFailure.Unreachable, $"the tool bridge at {Address} cannot be reached ({ex.Message}).", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ToolBridgeException(ToolBridgeFailure.Unreachable, $"the tool bridge at {Address} did not accept the connection in time.", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new ToolBridgeException(ToolBridgeFailure.TokenRefused, $"the tool bridge at {Address} refused the token.");
            try
            {
                if (!response.IsSuccessStatusCode)
                {
                    var refusal = await response.Content.ReadFromJsonAsync<BridgeErrorResponse>(ToolBridgeProtocol.JsonOptions, cancellationToken)
                        .ConfigureAwait(false);
                    throw new ToolBridgeException(ToolBridgeFailure.Refused,
                        $"the tool bridge at {Address} refused the request ({(int)response.StatusCode}): {refusal?.Error ?? "no reason given"}.");
                }

                return await response.Content.ReadFromJsonAsync<T>(ToolBridgeProtocol.JsonOptions, cancellationToken).ConfigureAwait(false)
                    ?? throw new ToolBridgeException(ToolBridgeFailure.Refused, $"the tool bridge at {Address} sent an empty answer.");
            }
            catch (JsonException ex)
            {
                throw new ToolBridgeException(ToolBridgeFailure.Refused, $"the tool bridge at {Address} sent an answer that is not its protocol.", ex);
            }
            catch (HttpRequestException ex)
            {
                throw new ToolBridgeException(ToolBridgeFailure.Unreachable, $"the connection to the tool bridge at {Address} broke ({ex.Message}).", ex);
            }
            catch (IOException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ToolBridgeException(ToolBridgeFailure.Unreachable, $"the connection to the tool bridge at {Address} broke ({ex.Message}).", ex);
            }
        }
    }
}
