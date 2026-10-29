using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Application.Decision;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Constants.Llm;
using Orkeon.Infrastructure.LLMs.Profiles;
using Orkeon.Infrastructure.Resilience;
using Polly;

namespace Orkeon.Infrastructure.Decision.TypeSafe;

/// <summary>
/// <see cref="IDecisionModel"/> over the TypeSafe System One API (<c>POST /systemone</c>,
/// <c>GET /models</c>). A request is validated before it is sent; a 429, a 529 or a network
/// error is retried with the LLM retry policy; each answered request is metered as a
/// <see cref="LlmUsageOperations.Decision"/> call. An error names what to fix, cites TypeSafe's
/// request id when the answer carries one, and never carries the key.
/// </summary>
public sealed partial class TypeSafeDecisionModel : IDecisionModel
{
    /// <summary>The header that admits images and box questions.</summary>
    public const string ImagesHeader = "TypeSafe-Danger-Zone-Images";

    /// <summary>The header that raises both context limits.</summary>
    public const string LongContextHeader = "TypeSafe-Danger-Zone-Long-Context";

    /// <summary>The response header that carries the id TypeSafe gave the request, cited by every error.</summary>
    public const string RequestIdHeader = "x-typesafe-request-id";

    private static readonly DecisionRequestValidator s_validator = new();

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TypeSafeOptions _options;
    private readonly ILogger<TypeSafeDecisionModel> _logger;
    private readonly ILlmUsageSink? _usageSink;
    private readonly IAsyncPolicy<HttpResponseMessage> _policy;
    private readonly LlmKeyEnvironment _keyEnvironment;

    /// <summary>Initializes a new instance of <see cref="TypeSafeDecisionModel"/>.</summary>
    /// <param name="httpClientFactory">Creates the client named after this class.</param>
    /// <param name="options">The <c>Orkeon:TypeSafe</c> section.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="usageSink">Receives the tokens of each answered request; null meters nothing.</param>
    /// <param name="policy">The retry policy; null takes the LLM one with <see cref="TypeSafeOptions.MaxRetries"/>.</param>
    public TypeSafeDecisionModel(
        IHttpClientFactory httpClientFactory,
        IOptions<TypeSafeOptions> options,
        ILogger<TypeSafeDecisionModel> logger,
        ILlmUsageSink? usageSink = null,
        IAsyncPolicy<HttpResponseMessage>? policy = null)
        : this(httpClientFactory, options, logger, usageSink, policy, LlmKeyEnvironment.Machine)
    {
    }

    internal TypeSafeDecisionModel(
        IHttpClientFactory httpClientFactory,
        IOptions<TypeSafeOptions> options,
        ILogger<TypeSafeDecisionModel> logger,
        ILlmUsageSink? usageSink,
        IAsyncPolicy<HttpResponseMessage>? policy,
        LlmKeyEnvironment keyEnvironment)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(keyEnvironment);
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
        _usageSink = usageSink;
        _keyEnvironment = keyEnvironment;
        _policy = policy ?? ResiliencePolicies.GetLlmApiPolicy(logger, _options.MaxRetries);
    }

    /// <inheritdoc />
    public string Name => DecisionProviderKeys.TypeSafe;

    /// <inheritdoc />
    public async Task<DecisionResponse> DecideAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validation = s_validator.Validate(request);
        if (!validation.IsValid)
            throw new DecisionModelException($"Invalid decision request: {validation.GetErrorMessage()}", null, isTransient: false);

        var sent = request with { Model = string.IsNullOrWhiteSpace(request.Model) ? _options.Model : request.Model };
        var body = JsonSerializer.Serialize(sent);
        var response = await SendAsync(
            HttpMethod.Post,
            "systemone",
            body,
            message =>
            {
                if (sent.UsesImages)
                    message.Headers.Add(ImagesHeader, "true");
                if (sent.LongContext)
                    message.Headers.Add(LongContextHeader, "true");
            },
            cancellationToken,
            sent.LongContext).ConfigureAwait(false);

        var decision = Deserialize<DecisionResponse>(response, "systemone");
        if (decision.Answers is null || decision.Usage is null || string.IsNullOrEmpty(decision.Model))
            throw new DecisionModelException("TypeSafe returned a body without answers, usage or model on /systemone.", null, isTransient: false);
        Meter(decision);
        return decision;
    }

    /// <inheritdoc />
    public async Task<ImmutableList<DecisionModelDto>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Get, "models", null, null, cancellationToken).ConfigureAwait(false);
        return Deserialize<ModelList>(response, "models").Models;
    }

    private async Task<string> SendAsync(
        HttpMethod method,
        string path,
        string? body,
        Action<HttpRequestMessage>? decorate,
        CancellationToken cancellationToken,
        bool longContext = false)
    {
        var key = ResolveApiKey();
        var uri = new Uri(_options.BaseUrl.AbsoluteUri.TrimEnd('/') + "/" + path);
        var client = _httpClientFactory.CreateClient(nameof(TypeSafeDecisionModel));
        // A bound per attempt rather than HttpClient.Timeout: a factory may hand back a client
        // that has already sent, whose Timeout can no longer be set.
        var attemptTimeout = TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds));

        HttpResponseMessage response;
        try
        {
            response = await _policy.ExecuteAsync(async ct =>
            {
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
                attempt.CancelAfter(attemptTimeout);
                // One message per attempt: a sent HttpRequestMessage cannot be sent again.
                using var message = new HttpRequestMessage(method, uri);
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                if (body is not null)
                    message.Content = new StringContent(body, Encoding.UTF8, "application/json");
                decorate?.Invoke(message);
                return await client.SendAsync(message, attempt.Token).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new DecisionModelException($"TypeSafe could not be reached at {uri.Host}: {ex.Message}", null, isTransient: true, ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DecisionModelException(
                $"TypeSafe did not answer within {_options.TimeoutSeconds} s ({TypeSafeOptions.SectionName}:TimeoutSeconds).",
                null, isTransient: true, ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return text;
            var requestId = response.Headers.TryGetValues(RequestIdHeader, out var ids) ? ids.FirstOrDefault() : null;
            throw Failure(response.StatusCode, text, longContext, requestId);
        }
    }

    private string ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            return _options.ApiKey.Trim();
        var variable = string.IsNullOrWhiteSpace(_options.ApiKeyEnvVar)
            ? DecisionProviderDefaults.TypeSafeApiKeyVariable
            : _options.ApiKeyEnvVar.Trim();
        var key = _keyEnvironment.Process(variable);
        if (string.IsNullOrWhiteSpace(key))
        {
            try
            {
                key = _keyEnvironment.User(variable);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or PlatformNotSupportedException or IOException)
            {
                key = null;
            }
        }

        return string.IsNullOrWhiteSpace(key)
            ? throw new DecisionModelException(
                $"No TypeSafe API key: set the environment variable {variable}, or {TypeSafeOptions.SectionName}:ApiKey.",
                null, isTransient: false)
            : key.Trim();
    }

    private static DecisionModelException Failure(HttpStatusCode status, string body, bool longContext, string? requestId)
    {
        var code = (int)status;
        var detail = Detail(body);
        var message = code switch
        {
            400 when body.Contains("max_tokens_exceeded", StringComparison.Ordinal) => longContext
                ? $"TypeSafe refused the request: the state is over the model's long context limit ({Orkeon.Constants.Llm.DecisionLimits.LongContextTokens} tokens)."
                : $"TypeSafe refused the request: the state is over the model's context limit ({Orkeon.Constants.Llm.DecisionLimits.StandardContextTokens} tokens); set long context on the task to use {Orkeon.Constants.Llm.DecisionLimits.LongContextTokens}.",
            400 => $"TypeSafe refused the request (400): {detail}",
            401 => $"TypeSafe refused the API key (401): check {DecisionProviderDefaults.TypeSafeApiKeyVariable} or {TypeSafeOptions.SectionName}:ApiKey.",
            413 => "TypeSafe refused the request (413): the body is over 32 MiB.",
            415 => "TypeSafe refused the request encoding (415).",
            422 => $"TypeSafe found the request invalid (422): {detail}",
            429 => "TypeSafe rate limit reached (429) after the retries.",
            529 => "TypeSafe is overloaded (529) after the retries.",
            _ => $"TypeSafe answered {code}: {detail}",
        };
        if (!string.IsNullOrWhiteSpace(requestId))
            message += $" (TypeSafe request {requestId.Trim()})";
        return new DecisionModelException(message, code, isTransient: code is 429 or >= 500);
    }

    /// <summary>
    /// The <c>detail</c> of an error body: a string, or each validation error as
    /// <c>location: message</c>; the raw body, bounded, when it is not JSON.
    /// </summary>
    private static string Detail(string body)
    {
        try
        {
            var detail = JsonNode.Parse(body)?["detail"];
            switch (detail)
            {
                case JsonValue value:
                    return value.ToString();
                case JsonArray errors:
                    return string.Join("; ", errors.Select(e =>
                        $"{string.Join('.', e?["loc"]?.AsArray().Select(l => l?.ToString()) ?? [])}: {e?["msg"]}"));
                case JsonObject obj:
                    return obj.ToJsonString();
            }
        }
        catch (JsonException)
        {
            // Not JSON: the raw body below.
        }

        return body.Length > 300 ? body[..300] + "…" : body;
    }

    private static T Deserialize<T>(string body, string path)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body)
                ?? throw new DecisionModelException($"TypeSafe returned an empty body on /{path}.", null, isTransient: false);
        }
        catch (JsonException ex)
        {
            throw new DecisionModelException($"TypeSafe returned an unreadable body on /{path}: {ex.Message}", null, isTransient: false, ex);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031",
        Justification = "Host-sink fault barrier: usage accounting must never fail the decision it observes.")]
    private void Meter(DecisionResponse response)
    {
        // A usage without counts (the SDKs accept it) is metered as zero rather than refused.
        LogDecisionAnswered(response.Model, response.Answers.Count, response.Usage.InputTokens ?? 0);
        if (_usageSink is null)
            return;
        var attribution = LlmUsageScope.Current;
        try
        {
            _usageSink.Record(new CostUsageEvent
            {
                CrewId = attribution.CrewId,
                AgentId = attribution.AgentId,
                TaskId = attribution.TaskId,
                OperationType = LlmUsageOperations.Decision,
                Model = response.Model,
                Provider = Name,
                PromptTokens = response.Usage.InputTokens ?? 0,
                CompletionTokens = response.Usage.OutputTokens ?? 0,
            });
        }
        catch (Exception)
        {
            // Deliberately swallowed — see the fault-barrier justification above.
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "TypeSafe {Model} answered {Questions} question(s) for {InputTokens} input tokens")]
    private partial void LogDecisionAnswered(string model, int questions, int inputTokens);

    private sealed record ModelList
    {
        [System.Text.Json.Serialization.JsonPropertyName("models")]
        public ImmutableList<DecisionModelDto> Models { get; init; } = [];
    }
}
