using Orkeon.Constants.Llm;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Presets;

namespace Orkeon.Studio.Core.Llm;

/// <summary>
/// Reads the balance over HTTP where a vendor serves it to an inference key, and answers every
/// other provider from what its documentation says. The two tables below are the whole of that
/// knowledge: three verified HTTP dialects, and a verdict for each provider without one. Every
/// entry was checked against the vendor's own reference on 2026-09-24 — the sources are listed
/// under "Account balance" in <c>docs/reference/llm-providers-comparison.md</c>.
/// <para>
/// A dialect names a path from the host root, and the host is always the endpoint's own
/// (STUDIO-33 D-04): a key of Kimi's .cn platform is refused by the .ai host, so the probe never
/// picks a host by itself. The paths live here rather than in <c>Orkeon.Constants.Llm</c>
/// (D-03): Studio is their only reader.
/// </para>
/// </summary>
public sealed class HttpProviderBalanceProbe : IProviderBalanceProbe, IDisposable
{
    private const string UsDollar = "USD";
    private const string Yuan = "CNY";

    /// <summary>
    /// The providers whose balance an inference key reads, with the path and the reader of
    /// each. Every reader expects the example answer of the vendor's reference page.
    /// </summary>
    private static readonly Dictionary<string, BalanceDialect> Dialects = new(StringComparer.Ordinal)
    {
        // api-docs.deepseek.com/api/get-user-balance
        [LlmProviderKeys.DeepSeek] = new("/user/balance", (answer, _) => ReadDeepSeek(answer)),
        // platform.kimi.ai/docs/api/balance (api.moonshot.ai) and its twin
        // platform.kimi.com/docs/api/balance (api.moonshot.cn)
        [LlmProviderKeys.Kimi] = new("/v1/users/me/balance", ReadKimi),
        // openrouter.ai/docs/api/api-reference/api-keys/get-current-api-key
        [LlmProviderKeys.OpenRouter] = new("/api/v1/key", (answer, _) => ReadOpenRouter(answer)),
    };

    /// <summary>The providers answered without a request: what their documentation settles.</summary>
    private static readonly Dictionary<string, BalanceReading> Verdicts = new(StringComparer.Ordinal)
    {
        [LlmProviderKeys.OpenAI] = BalanceReading.Of(ProviderBalanceStatus.NotExposed,
            "OpenAI serves no balance: its Costs API reports spend, and to an admin key only."),
        [LlmProviderKeys.AzureOpenAI] = BalanceReading.Of(ProviderBalanceStatus.NotExposed,
            "Azure serves no balance to a resource key: spend is in Cost Management, for a Microsoft Entra identity."),
        [LlmProviderKeys.Anthropic] = BalanceReading.Of(ProviderBalanceStatus.NotExposed,
            "Anthropic serves no balance: its Cost API reports spend, and to an Admin API key only."),
        [LlmProviderKeys.Mistral] = BalanceReading.Of(ProviderBalanceStatus.NotExposed,
            "Mistral serves no balance: its Admin API reports usage, and to an Admin API key only."),
        [LlmProviderKeys.Gemini] = BalanceReading.Of(ProviderBalanceStatus.NotExposed,
            "Gemini serves no balance: the prepay balance is managed in Google AI Studio only."),
        [LlmProviderKeys.Together] = BalanceReading.Of(ProviderBalanceStatus.NotExposed,
            "Together AI serves no balance: the credit balance is shown in its dashboard only."),
        [LlmProviderKeys.HuggingFace] = BalanceReading.Of(ProviderBalanceStatus.NotExposed,
            "HuggingFace serves no balance: credits and spend are shown on its billing page only."),
        [LlmProviderKeys.Zai] = BalanceReading.Of(ProviderBalanceStatus.NotExposed,
            "Z.AI serves no balance: its API reference has no billing endpoint."),
        [LlmProviderKeys.MiniMax] = BalanceReading.Of(ProviderBalanceStatus.NotExposed,
            "MiniMax serves no pay-as-you-go balance; its Token Plan endpoint takes a subscription key and documents no answer."),
        [LlmProviderKeys.Mammouth] = BalanceReading.Of(ProviderBalanceStatus.NotExposed,
            "Mammouth documents the spend of a key without the shape of the answer; usage and cost are in its account settings."),
        [LlmProviderKeys.Grok] = BalanceReading.Of(ProviderBalanceStatus.AdminKeyRequired,
            "x.AI serves the prepaid balance on its Management API, to a management key only."),
        [LlmProviderKeys.Qwen] = BalanceReading.Of(ProviderBalanceStatus.AdminKeyRequired,
            "Alibaba Cloud serves the account balance through its billing API (QueryAccountBalance), to an account AccessKey only."),
        [LlmProviderKeys.Ollama] = BalanceReading.Of(ProviderBalanceStatus.NotApplicable,
            "A local runtime has no account to ask."),
        [LlmProviderKeys.DockerModelRunner] = BalanceReading.Of(ProviderBalanceStatus.NotApplicable,
            "A local runtime has no account to ask."),
        [LlmProviderKeys.None] = BalanceReading.Of(ProviderBalanceStatus.NotApplicable,
            "No endpoint is configured, so there is no account to ask."),
    };

    private static readonly BalanceReading LocalEndpoint = BalanceReading.Of(ProviderBalanceStatus.NotApplicable,
        "A local endpoint has no account to ask.");

    private static readonly BalanceReading UnknownEndpoint = BalanceReading.Of(ProviderBalanceStatus.NotExposed,
        "Studio knows no balance endpoint for this host.");

    private static readonly BalanceReading UnexpectedShape = BalanceReading.Of(ProviderBalanceStatus.UnexpectedAnswer,
        "The provider answered outside its documented shape: its balance endpoint may have changed.");

    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly TimeProvider _time;

    /// <summary>Probes through <paramref name="client"/>, which the caller keeps ownership of.</summary>
    /// <param name="client">The client every request goes through.</param>
    /// <param name="timeProvider">Stamps <see cref="ProviderBalanceResult.CheckedAt"/>; the system clock by default.</param>
    public HttpProviderBalanceProbe(HttpClient client, TimeProvider? timeProvider = null)
        : this(client, ownsClient: false, timeProvider)
    {
    }

    private HttpProviderBalanceProbe(HttpClient client, bool ownsClient, TimeProvider? timeProvider)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _ownsClient = ownsClient;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Probes over the real network, through a client this probe owns.</summary>
    public static HttpProviderBalanceProbe ForCurrentMachine() => new(new HttpClient(), ownsClient: true, timeProvider: null);

    /// <summary>
    /// The providers whose balance an inference key reads — the verified dialects, by name.
    /// Every other provider answers without an amount, so a threshold on one of these is the
    /// only kind that can ever fire (STUDIO-35 D-03).
    /// </summary>
    public static IReadOnlyList<string> ReadableProviders { get; } = [.. Dialects.Keys.Order(StringComparer.Ordinal)];

    /// <inheritdoc />
    public async Task<ProviderBalanceResult> ProbeAsync(
        LlmProbeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var provider = LlmProviderDetector.Detect(request.BaseUrl);
        var reading = await ReadAsync(provider, request, cancellationToken).ConfigureAwait(false);

        return new ProviderBalanceResult
        {
            Provider = provider,
            Status = reading.Status,
            Amounts = reading.Amounts,
            Scope = reading.Scope,
            CheckedAt = _time.GetUtcNow(),
            Detail = Mask(reading.Detail, request.ApiKey),
            ConsoleUrl = LlmPresets.KeyConsoleFor(request.BaseUrl),
        };
    }

    /// <summary>Answers from the verdict table, or asks the provider in its verified dialect.</summary>
    [SuppressMessage("Design", "CA1031",
        Justification = "The probe's contract is a typed result, never an exception: a status bar that " +
                        "shows a balance must report what went wrong, not fault.")]
    private async Task<BalanceReading> ReadAsync(
        string provider,
        LlmProbeRequest request,
        CancellationToken cancellationToken)
    {
        if (Verdicts.TryGetValue(provider, out var verdict))
            return verdict;

        if (!Dialects.TryGetValue(provider, out var dialect)
            || !Uri.TryCreate(request.BaseUrl?.Trim(), UriKind.Absolute, out var baseUri))
        {
            return IsLocal(request.BaseUrl) ? LocalEndpoint : UnknownEndpoint;
        }

        var apiKey = request.ApiKey?.Trim();
        if (string.IsNullOrEmpty(apiKey))
            return BalanceReading.Of(ProviderBalanceStatus.AuthenticationRefused, "No API key is set, so there is no account to ask.");

        // The probe owns its deadline rather than the client's, so a shared HttpClient
        // (and the request timeout it carries) is none of this method's business.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(request.Timeout);

        try
        {
            // An absolute path replaces the base URL's own: "/v1" and the like are dropped.
            using var message = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, dialect.Path));
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await _client.SendAsync(message, deadline.Token).ConfigureAwait(false);

            // The body is read only once the status says it holds a balance: an error answer
            // may carry account data, and none of it is quoted back (D-05).
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return BalanceReading.Of(ProviderBalanceStatus.AuthenticationRefused, $"The provider refused the key ({StatusLine(response)}).");

            if (response.StatusCode == HttpStatusCode.NotFound)
                return BalanceReading.Of(ProviderBalanceStatus.NotExposed, $"The host answered {StatusLine(response)}: it serves no balance at {dialect.Path}.");

            if (!response.IsSuccessStatusCode)
                return BalanceReading.Of(ProviderBalanceStatus.NetworkError, $"The provider answered {StatusLine(response)}.");

            var body = await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);
            return Read(dialect, body, baseUri);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller asked to stop — that is not a verdict about the provider.
            throw;
        }
        catch (OperationCanceledException)
        {
            return BalanceReading.Of(ProviderBalanceStatus.NetworkError, string.Create(
                CultureInfo.InvariantCulture,
                $"No answer within {request.Timeout.TotalSeconds:0.#}s."));
        }
        catch (Exception ex)
        {
            return BalanceReading.Of(ProviderBalanceStatus.NetworkError, ex.Message);
        }
    }

    /// <summary>Parses a successful answer with the dialect's reader.</summary>
    private static BalanceReading Read(BalanceDialect dialect, string body, Uri baseUri)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return dialect.Read(document.RootElement, baseUri) ?? UnexpectedShape;
        }
        catch (JsonException)
        {
            return UnexpectedShape;
        }
    }

    /// <summary>
    /// DeepSeek, <c>GET /user/balance</c>: one entry per currency, amounts as decimal strings.
    /// <code>{ "is_available": true, "balance_infos": [ { "currency": "CNY", "total_balance": "110.00",
    ///   "granted_balance": "10.00", "topped_up_balance": "100.00" } ] }</code>
    /// </summary>
    private static BalanceReading? ReadDeepSeek(JsonElement answer)
    {
        if (!TryGetProperty(answer, "balance_infos", out var infos) || infos.ValueKind != JsonValueKind.Array)
            return null;

        var amounts = new List<ProviderBalanceAmount>();
        foreach (var info in infos.EnumerateArray())
        {
            if (!TryGetProperty(info, "currency", out var currency)
                || currency.ValueKind != JsonValueKind.String
                || !TryGetAmount(info, "total_balance", out var total))
            {
                return null;
            }

            amounts.Add(new ProviderBalanceAmount(
                currency.GetString()!,
                total,
                Granted: OptionalAmount(info, "granted_balance"),
                Paid: OptionalAmount(info, "topped_up_balance")));
        }

        return amounts.Count == 0 ? null : BalanceReading.Available(amounts, ProviderBalanceScope.Account);
    }

    /// <summary>
    /// Kimi, <c>GET /v1/users/me/balance</c>: plain numbers, and no currency in the answer — each
    /// platform documents its own, US dollars on the .ai host and yuan on the .cn one.
    /// <code>{ "code": 0, "data": { "available_balance": 49.58894, "voucher_balance": 46.58893,
    ///   "cash_balance": 3.00001 }, "scode": "0x0", "status": true }</code>
    /// </summary>
    private static BalanceReading? ReadKimi(JsonElement answer, Uri baseUri)
    {
        if (!TryGetProperty(answer, "data", out var data) || !TryGetAmount(data, "available_balance", out var available))
            return null;

        var currency = string.Equals(baseUri.Host, LlmProviderEndpoints.KimiChinaHost, StringComparison.OrdinalIgnoreCase)
            ? Yuan
            : UsDollar;

        return BalanceReading.Available(
            [
                new ProviderBalanceAmount(
                    currency,
                    available,
                    Granted: OptionalAmount(data, "voucher_balance"),
                    Paid: OptionalAmount(data, "cash_balance")),
            ],
            ProviderBalanceScope.Account);
    }

    /// <summary>
    /// OpenRouter, <c>GET /api/v1/key</c>: what the key may still spend under its limit, in US
    /// dollars. Null when the key has no limit — the account's credits are then on
    /// <c>GET /api/v1/credits</c>, which answers 403 to anything but a management key.
    /// <code>{ "data": { "limit": 100, "limit_remaining": 74.5, "usage": 25.5, … } }</code>
    /// </summary>
    private static BalanceReading? ReadOpenRouter(JsonElement answer)
    {
        if (!TryGetProperty(answer, "data", out var data) || !TryGetProperty(data, "limit_remaining", out var remaining))
            return null;

        if (remaining.ValueKind == JsonValueKind.Null)
        {
            return BalanceReading.Of(ProviderBalanceStatus.AdminKeyRequired,
                "This key has no spending limit, and OpenRouter serves the account's credits to a management key only.");
        }

        return TryGetAmount(remaining, out var amount)
            ? BalanceReading.Available([new ProviderBalanceAmount(UsDollar, amount)], ProviderBalanceScope.ApiKey)
            : null;
    }

    /// <summary>Reads a property of an object; false for a missing property or a non-object.</summary>
    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value);
    }

    /// <summary>Reads an amount property, written as a JSON number or as a decimal string.</summary>
    private static bool TryGetAmount(JsonElement element, string name, out decimal amount)
    {
        amount = 0;
        return TryGetProperty(element, name, out var value) && TryGetAmount(value, out amount);
    }

    /// <summary>Reads an amount written as a JSON number or as a decimal string.</summary>
    private static bool TryGetAmount(JsonElement value, out decimal amount)
    {
        amount = 0;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetDecimal(out amount),
            JsonValueKind.String => decimal.TryParse(
                value.GetString(),
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out amount),
            _ => false,
        };
    }

    /// <summary>An optional part of a balance: null when absent or unreadable.</summary>
    private static decimal? OptionalAmount(JsonElement element, string name) =>
        TryGetAmount(element, name, out var amount) ? amount : null;

    /// <summary>An endpoint on this machine or its Docker host: no account behind it.</summary>
    private static bool IsLocal(string? baseUrl) =>
        Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out var uri)
        && (uri.IsLoopback || string.Equals(uri.Host, "host.docker.internal", StringComparison.OrdinalIgnoreCase));

    /// <summary>The status line of an answer: the code and its standard name, never the server's text.</summary>
    private static string StatusLine(HttpResponseMessage response) =>
        string.Create(CultureInfo.InvariantCulture, $"{(int)response.StatusCode} {response.StatusCode}");

    /// <summary>Masks the key wherever a detail repeats it — an exception message included.</summary>
    private static string Mask(string detail, string? apiKey)
    {
        var key = apiKey?.Trim();
        return string.IsNullOrEmpty(key) ? detail : detail.Replace(key, "***", StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsClient)
            _client.Dispose();
    }

    /// <summary>A verified HTTP dialect: the path from the host root, and the reader of the answer.</summary>
    /// <param name="Path">Absolute path of the balance endpoint on the endpoint's host.</param>
    /// <param name="Read">
    /// Turns the answer into a reading, or null when it is not in the documented shape. It also
    /// gets the endpoint, for the one answer whose meaning depends on the host (Kimi's currency).
    /// </param>
    private sealed record BalanceDialect(string Path, Func<JsonElement, Uri, BalanceReading?> Read);

    /// <summary>What the probe learned, before it is stamped and attributed.</summary>
    private sealed record BalanceReading(
        ProviderBalanceStatus Status,
        string Detail,
        IReadOnlyList<ProviderBalanceAmount> Amounts,
        ProviderBalanceScope Scope = ProviderBalanceScope.Account)
    {
        /// <summary>A reading with no balance to tell.</summary>
        public static BalanceReading Of(ProviderBalanceStatus status, string detail) => new(status, detail, []);

        /// <summary>A balance, with a detail that states it.</summary>
        public static BalanceReading Available(IReadOnlyList<ProviderBalanceAmount> amounts, ProviderBalanceScope scope)
        {
            var stated = string.Join(", ", amounts.Select(a =>
                string.Create(CultureInfo.InvariantCulture, $"{a.Available} {a.Currency}")));

            return new BalanceReading(
                ProviderBalanceStatus.Available,
                scope == ProviderBalanceScope.ApiKey ? $"{stated} left under this key's limit." : $"{stated} available.",
                amounts,
                scope);
        }
    }
}
