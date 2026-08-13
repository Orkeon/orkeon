using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Orkeon.Studio.Core.Configuration;

namespace Orkeon.Studio.Core.Llm;

/// <summary>
/// Reaches the endpoint over HTTP and asks it for its model catalogue — the lightest request
/// that proves the host answers and accepts the key. The dialect follows the provider inferred
/// from the URL by <see cref="LlmProviderDetector"/>: <c>GET {baseUrl}/models</c> with a bearer
/// token for the OpenAI-compatible majority, <c>GET {root}/api/tags</c> for Ollama, and
/// Anthropic's <c>x-api-key</c> header for Anthropic.
/// <para>
/// This duplicates, in miniature, what <c>orkeon init</c>'s probe does. It cannot share that
/// code: the CLI's <c>LlmCatalogClient</c> lives in <c>Orkeon.Scripting.Cli</c>, which drags in
/// the whole runtime — Studio Core deliberately references neither Infrastructure nor Hosting
/// (see the note in <c>Orkeon.Studio.Core.csproj</c>).
/// </para>
/// </summary>
public sealed class HttpLlmEndpointProbe : ILlmEndpointProbe, IDisposable
{
    /// <summary>How much of an error body is quoted back to the user.</summary>
    private const int MaxQuotedBodyLength = 200;

    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    /// <summary>Probes through <paramref name="client"/>, which the caller keeps ownership of.</summary>
    public HttpLlmEndpointProbe(HttpClient client)
        : this(client, ownsClient: false)
    {
    }

    private HttpLlmEndpointProbe(HttpClient client, bool ownsClient)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _ownsClient = ownsClient;
    }

    /// <summary>Probes over the real network, through a client this probe owns.</summary>
    public static HttpLlmEndpointProbe ForCurrentMachine() => new(new HttpClient(), ownsClient: true);

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031",
        Justification = "The probe's contract is a typed result, never an exception: a UI button " +
                        "that tests connectivity must report what went wrong, not fault.")]
    public async Task<LlmProbeResult> ProbeAsync(
        LlmProbeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.BaseUrl))
            return LlmProbeResult.Unreachable("no base URL is configured, so there is nothing to reach.");

        if (!Uri.TryCreate(request.BaseUrl.Trim(), UriKind.Absolute, out var baseUri))
        {
            return LlmProbeResult.Unreachable(string.Create(
                CultureInfo.InvariantCulture,
                $"'{request.BaseUrl}' is not an absolute URL."));
        }

        // A host typed without its scheme ("localhost:11434") parses as an absolute URI whose
        // scheme is "localhost" — accepted here, it would only fail deep inside HttpClient.
        if (!string.Equals(baseUri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            && !string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            return LlmProbeResult.Unreachable(string.Create(
                CultureInfo.InvariantCulture,
                $"'{request.BaseUrl}' is not an http:// or https:// URL."));
        }

        var provider = LlmProviderDetector.Detect(request.BaseUrl);
        if (string.Equals(provider, LlmProviderDetector.AzureOpenAI, StringComparison.Ordinal))
        {
            return LlmProbeResult.Unreachable(
                "Azure OpenAI serves named deployments rather than a model catalogue, "
                + "so there is no endpoint to probe.");
        }

        // The probe owns its deadline rather than the client's, so a shared HttpClient
        // (and the request timeout it carries) is none of this method's business.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(request.Timeout);

        try
        {
            using var message = BuildRequest(provider, baseUri, request.ApiKey);
            using var response = await _client.SendAsync(message, deadline.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return LlmProbeResult.Unreachable(string.Create(
                    CultureInfo.InvariantCulture,
                    $"the endpoint answered {(int)response.StatusCode} {response.ReasonPhrase}. {Shorten(body)}"));
            }

            return LlmProbeResult.Reachable(TryCountModels(provider, body));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller asked to stop — that is not a verdict about the endpoint.
            throw;
        }
        catch (OperationCanceledException)
        {
            return LlmProbeResult.Unreachable(string.Create(
                CultureInfo.InvariantCulture,
                $"no answer within {request.Timeout.TotalSeconds:0.#}s."));
        }
        catch (Exception ex)
        {
            return LlmProbeResult.Unreachable(ex.Message);
        }
    }

    /// <summary>Builds the catalogue request in the dialect the detected provider speaks.</summary>
    private static HttpRequestMessage BuildRequest(string provider, Uri baseUri, string? apiKey)
    {
        var baseUrl = baseUri.AbsoluteUri.TrimEnd('/');

        if (string.Equals(provider, LlmProviderDetector.Ollama, StringComparison.Ordinal))
        {
            // Ollama's catalogue hangs off the server root, not off an OpenAI-style
            // /v1 base — so a base URL carrying a path must have it dropped.
            var root = baseUri.GetLeftPart(UriPartial.Authority);
            return new HttpRequestMessage(HttpMethod.Get, $"{root}/api/tags");
        }

        if (string.Equals(provider, AnthropicProvider, StringComparison.Ordinal))
        {
            var anthropic = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/v1/models");
            anthropic.Headers.Add("x-api-key", apiKey ?? "");
            anthropic.Headers.Add("anthropic-version", AnthropicVersion);
            return anthropic;
        }

        var message = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/models");
        if (!string.IsNullOrWhiteSpace(apiKey))
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        return message;
    }

    /// <summary>
    /// Counts the models in a successful answer, or returns null when the payload is not in a
    /// shape we know. An unparsable body is not a failure: the endpoint answered.
    /// </summary>
    [SuppressMessage("Design", "CA1031",
        Justification = "Counting models is a nicety on top of a successful probe; any parsing " +
                        "failure degrades to 'reachable, count unknown'.")]
    private static int? TryCountModels(string provider, string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var arrayName = string.Equals(provider, LlmProviderDetector.Ollama, StringComparison.Ordinal)
                ? "models"   // Ollama: { "models": [ { "name": … } ] }
                : "data";    // OpenAI-compatible: { "data": [ { "id": … } ] }

            return document.RootElement.TryGetProperty(arrayName, out var models)
                && models.ValueKind == JsonValueKind.Array
                ? models.GetArrayLength()
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Trims an error body to something that fits on a status line.</summary>
    private static string Shorten(string body)
    {
        var text = body.Trim();
        if (text.Length == 0)
            return "";

        return text.Length <= MaxQuotedBodyLength
            ? text
            : string.Concat(text.AsSpan(0, MaxQuotedBodyLength), "…");
    }

    /// <summary>Provider key <see cref="LlmProviderDetector"/> reports for the Anthropic host.</summary>
    private const string AnthropicProvider = "anthropic";

    /// <summary>The version header every Anthropic request must carry.</summary>
    private const string AnthropicVersion = "2023-06-01";

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsClient)
            _client.Dispose();
    }
}
