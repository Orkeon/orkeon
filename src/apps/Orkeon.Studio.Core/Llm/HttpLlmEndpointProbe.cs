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
/// When the request asks for it (<see cref="LlmProbeRequest.CheckCompletion"/>), a reachable
/// catalogue is followed by a minimal completion on the profile's model, with its thinking
/// switch, written by <see cref="LlmProbeDialect"/> (STUDIO-43). A failure names its step, the
/// URL called (never the key), the time waited and the innermost cause.
/// </para>
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

    /// <summary>What stands in the result wherever the key would have appeared.</summary>
    private const string RedactedKey = "***";

    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly TimeProvider _time;

    /// <summary>Probes through <paramref name="client"/>, which the caller keeps ownership of.</summary>
    public HttpLlmEndpointProbe(HttpClient client)
        : this(client, ownsClient: false, time: null)
    {
    }

    /// <summary>
    /// Probes through <paramref name="client"/> on <paramref name="time"/>'s clock — the deadline
    /// and the elapsed time follow it, so a test can simulate a slow endpoint without waiting.
    /// </summary>
    public HttpLlmEndpointProbe(HttpClient client, TimeProvider? time)
        : this(client, ownsClient: false, time)
    {
    }

    private HttpLlmEndpointProbe(HttpClient client, bool ownsClient, TimeProvider? time)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _ownsClient = ownsClient;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Probes over the real network, through a client this probe owns.</summary>
    public static HttpLlmEndpointProbe ForCurrentMachine() => new(new HttpClient(), ownsClient: true, time: null);

    /// <inheritdoc />
    public async Task<LlmProbeResult> ProbeAsync(
        LlmProbeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.BaseUrl))
            return Refused(LlmProbeFailure.NoBaseUrl, null);

        if (!Uri.TryCreate(request.BaseUrl.Trim(), UriKind.Absolute, out var baseUri))
            return Refused(LlmProbeFailure.NotAbsoluteUrl, Redact(request.BaseUrl, request.ApiKey));

        // A host typed without its scheme ("localhost:11434") parses as an absolute URI whose
        // scheme is "localhost" — accepted here, it would only fail deep inside HttpClient.
        if (!string.Equals(baseUri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            && !string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            return Refused(LlmProbeFailure.NotHttpUrl, Redact(request.BaseUrl, request.ApiKey));
        }

        var provider = LlmProviderDetector.Detect(request.BaseUrl);
        if (string.Equals(provider, LlmProviderDetector.AzureOpenAI, StringComparison.Ordinal))
            return Refused(LlmProbeFailure.NoCatalogue, null);

        // The probe owns its deadline rather than the client's, so a shared HttpClient (and the
        // request timeout it carries) is none of this method's business. One deadline covers
        // both steps: it is what the user waits for.
        var started = _time.GetTimestamp();
        using var deadline = new CancellationTokenSource(request.Timeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var context = new StepContext(request, started);

        var catalogue = await RunStepAsync(
            LlmProbeStage.Models, () => BuildRequest(provider, baseUri, request.ApiKey), context, linked.Token, cancellationToken)
            .ConfigureAwait(false);
        if (!catalogue.Succeeded)
            return catalogue.Result!;

        var modelCount = TryCountModels(provider, catalogue.Body!);
        if (!request.CheckCompletion || string.IsNullOrWhiteSpace(request.Model))
        {
            return LlmProbeResult.Reachable(modelCount) with
            {
                Url = catalogue.Url,
                Elapsed = _time.GetElapsedTime(started),
                Timeout = request.Timeout,
            };
        }

        var model = request.Model.Trim();
        var completion = await RunStepAsync(
            LlmProbeStage.Completion,
            () => LlmProbeDialect.BuildCompletion(provider, baseUri, request.ApiKey, model, request.ThinkingEnabled),
            context, linked.Token, cancellationToken).ConfigureAwait(false);
        if (!completion.Succeeded)
            return completion.Result!;

        return new LlmProbeResult
        {
            Succeeded = true,
            Stage = LlmProbeStage.Completion,
            Url = completion.Url,
            Elapsed = _time.GetElapsedTime(started),
            Timeout = request.Timeout,
            ModelCount = modelCount,
            Model = model,
        };
    }

    /// <summary>What a step needs to word its verdict.</summary>
    private sealed record StepContext(LlmProbeRequest Request, long Started);

    /// <summary>A step's outcome: its body on success, its failed result otherwise.</summary>
    private sealed record StepOutcome(bool Succeeded, string? Url, string? Body, LlmProbeResult? Result);

    /// <summary>Sends one step and turns every failure into a result naming the step.</summary>
    [SuppressMessage("Design", "CA1031",
        Justification = "The probe's contract is a typed result, never an exception: a UI button " +
                        "that tests connectivity must report what went wrong, not fault.")]
    private async Task<StepOutcome> RunStepAsync(
        LlmProbeStage stage,
        Func<HttpRequestMessage> build,
        StepContext context,
        CancellationToken token,
        CancellationToken callerToken)
    {
        string? url = null;
        try
        {
            using var message = build();
            url = DisplayUrl(message.RequestUri, context.Request.ApiKey);
            using var response = await _client.SendAsync(message, token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
                return new StepOutcome(true, url, body, null);

            var detail = Shorten(Redact(body, context.Request.ApiKey));
            return new StepOutcome(false, url, null,
                Failure(stage, url, context, LlmProbeFailure.HttpStatus, detail) with
                {
                    StatusCode = (int)response.StatusCode,
                    ReasonPhrase = response.ReasonPhrase,
                });
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            // The caller asked to stop — that is not a verdict about the endpoint.
            throw;
        }
        catch (OperationCanceledException)
        {
            return Failed(stage, url, context, LlmProbeFailure.Timeout, null);
        }
        catch (Exception ex)
        {
            return Failed(stage, url, context, LlmProbeFailure.Transport, Redact(Chain(ex), context.Request.ApiKey));
        }
    }

    private StepOutcome Failed(LlmProbeStage stage, string? url, StepContext context, LlmProbeFailure failure, string? detail) =>
        new(false, url, null, Failure(stage, url, context, failure, detail));

    private LlmProbeResult Failure(
        LlmProbeStage stage, string? url, StepContext context, LlmProbeFailure failure, string? detail) => new()
    {
        Succeeded = false,
        Stage = stage,
        Failure = failure,
        Url = url,
        Elapsed = _time.GetElapsedTime(context.Started),
        Timeout = context.Request.Timeout,
        Detail = detail,
    };

    /// <summary>A refusal before any request: the configuration itself cannot be probed.</summary>
    private static LlmProbeResult Refused(LlmProbeFailure failure, string? detail) => new()
    {
        Succeeded = false,
        Stage = LlmProbeStage.Configuration,
        Failure = failure,
        Detail = detail,
    };

    /// <summary>
    /// "Type: message", then the innermost exception the same way when it is another one —
    /// the socket or TLS error a bare <c>HttpRequestException.Message</c> hides.
    /// </summary>
    private static string Chain(Exception ex)
    {
        var innermost = ex;
        while (innermost.InnerException is { } inner)
            innermost = inner;

        var outer = $"{ex.GetType().Name}: {ex.Message}";
        return ReferenceEquals(innermost, ex)
            ? outer
            : $"{outer} ← {innermost.GetType().Name}: {innermost.Message}";
    }

    /// <summary>The URL as shown: no user info, no query string, no fragment — nothing that can carry a key.</summary>
    private static string? DisplayUrl(Uri? uri, string? apiKey) =>
        uri is null ? null : Redact(uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped), apiKey);

    /// <summary>Replaces every occurrence of the key in <paramref name="text"/>.</summary>
    private static string Redact(string text, string? apiKey) =>
        string.IsNullOrWhiteSpace(apiKey) ? text : text.Replace(apiKey.Trim(), RedactedKey, StringComparison.Ordinal);

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
            anthropic.Headers.Add("anthropic-version", LlmProbeDialect.AnthropicVersion);
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


    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsClient)
            _client.Dispose();
    }
}
