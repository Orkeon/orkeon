using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Tools.Web.Firefox;
using Orkeon.Tools.Web.Firefox.BiDi;

namespace Orkeon.Tools.Web.Chromium;

/// <summary>
/// <see cref="IBiDiTransport"/> over the DevTools protocol of a Chromium browser: the chromium-bidi mapper
/// runs in a hidden tab, takes each BiDi message through <c>Runtime.evaluate</c> and gives its answers and
/// events back through a binding. The mapper leaves its own tab out of everything it reports.
/// </summary>
internal sealed class ChromiumBiDiTransport : IBiDiTransport
{
    private const string Binding = "sendBidiResponse";

    private static readonly TimeSpan s_firstPagePoll = TimeSpan.FromMilliseconds(50);

    private readonly IBiDiTransport _channel;
    private string _sessionId = string.Empty;
    private string _targetId = string.Empty;
    private int _lastId;

    private ChromiumBiDiTransport(IBiDiTransport channel) => _channel = channel;

    /// <summary>Connects to the browser's DevTools <paramref name="endpoint"/> and starts the mapper in it.</summary>
    /// <exception cref="FirefoxException">The browser refused a step of the start, or went away.</exception>
    [SuppressMessage("Reliability", "CA2000", Justification = "The channel is handed to the transport returned, which disposes it; a start that fails disposes it.")]
    public static async Task<ChromiumBiDiTransport> ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        var channel = await WebSocketBiDiTransport.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
        return await StartAsync(channel, ChromiumMapper.Script, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Starts the mapper on an open DevTools channel, which the transport returned owns.</summary>
    internal static async Task<ChromiumBiDiTransport> StartAsync(IBiDiTransport channel, string script, CancellationToken cancellationToken)
    {
        var transport = new ChromiumBiDiTransport(channel);
        try
        {
            await transport.StartMapperAsync(script, cancellationToken).ConfigureAwait(false);
            return transport;
        }
        catch
        {
            await transport.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Task SendAsync(string message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        // The DevTools command carries the BiDi message's id: one the mapper never took is answered in its name (Refusal).
        var id = BiDiId(message);
        return _channel.SendAsync(
            Command(id, "Runtime.evaluate", new JsonObject { ["expression"] = $"onBidiMessage({JsonSerializer.Serialize(message)})" }, _sessionId),
            cancellationToken);
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        while (await _channel.ReceiveAsync(cancellationToken).ConfigureAwait(false) is { } text)
        {
            if (JsonNode.Parse(text) is not JsonObject message)
                continue;
            if (Payload(message) is { } payload)
                return payload;
            if (Refusal(message) is { } refusal)
                return refusal;
            if (MapperGone(message))
                return null;
        }

        return null;
    }

    public ValueTask DisposeAsync() => _channel.DisposeAsync();

    private async Task StartMapperAsync(string script, CancellationToken cancellationToken)
    {
        // The endpoint is announced before the first tab exists: the session needs one to drive.
        while (!await HasPageAsync(cancellationToken).ConfigureAwait(false))
            await Task.Delay(s_firstPagePoll, cancellationToken).ConfigureAwait(false);

        var created = await CallAsync(
            "Target.createTarget", new JsonObject { ["url"] = "about:blank#orkeon-mapper", ["hidden"] = true }, null, cancellationToken)
            .ConfigureAwait(false);
        _targetId = Text(created, "targetId");
        var attached = await CallAsync(
            "Target.attachToTarget", new JsonObject { ["targetId"] = _targetId, ["flatten"] = true }, null, cancellationToken)
            .ConfigureAwait(false);
        _sessionId = Text(attached, "sessionId");

        await CallAsync("Runtime.enable", [], _sessionId, cancellationToken).ConfigureAwait(false);
        await CallAsync("Target.exposeDevToolsProtocol", new JsonObject
        {
            ["bindingName"] = "cdp",
            ["targetId"] = _targetId,
            ["inheritPermissions"] = true,
        }, null, cancellationToken).ConfigureAwait(false);
        await CallAsync("Runtime.addBinding", new JsonObject { ["name"] = Binding }, _sessionId, cancellationToken).ConfigureAwait(false);
        await EvaluateAsync(script, cancellationToken).ConfigureAwait(false);
        await EvaluateAsync(
            $"window.runMapperInstance({JsonSerializer.Serialize(_targetId)}).then(() => true)", cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> HasPageAsync(CancellationToken cancellationToken)
    {
        var targets = await CallAsync("Target.getTargets", [], null, cancellationToken).ConfigureAwait(false);
        return targets["targetInfos"] is JsonArray infos
            && infos.OfType<JsonObject>().Any(info => info["type"]?.GetValue<string>() == "page");
    }

    private async Task EvaluateAsync(string expression, CancellationToken cancellationToken)
    {
        var answer = await CallAsync("Runtime.evaluate", new JsonObject
        {
            ["expression"] = expression,
            ["awaitPromise"] = true,
            ["returnByValue"] = true,
        }, _sessionId, cancellationToken).ConfigureAwait(false);
        if (answer["exceptionDetails"] is JsonObject details)
            throw new FirefoxException($"The mapper (chromium-bidi {ChromiumMapper.Version}) did not start: {Describe(details)}.");
    }

    /// <summary>One command of the start, before the receive loop of the connection runs: it reads its own answer.</summary>
    private async Task<JsonObject> CallAsync(string method, JsonObject parameters, string? sessionId, CancellationToken cancellationToken)
    {
        // Negative ids: the BiDi messages that follow use their own, which are not.
        long id = -(++_lastId);
        await _channel.SendAsync(Command(id, method, parameters, sessionId), cancellationToken).ConfigureAwait(false);
        while (await _channel.ReceiveAsync(cancellationToken).ConfigureAwait(false) is { } text)
        {
            if (JsonNode.Parse(text) is not JsonObject message || Id(message) != id)
                continue;
            return message["error"] is JsonObject error
                ? throw new FirefoxException($"The browser refused '{method}': {error["message"]}.")
                : message["result"] as JsonObject ?? [];
        }

        throw new FirefoxException($"The browser closed its DevTools channel during '{method}'.");
    }

    /// <summary>The BiDi message a binding call carries.</summary>
    private string? Payload(JsonObject message) =>
        message["method"]?.GetValue<string>() == "Runtime.bindingCalled"
        && message["sessionId"]?.GetValue<string>() == _sessionId
        && message["params"] is JsonObject parameters
        && parameters["name"]?.GetValue<string>() == Binding
            ? parameters["payload"]?.GetValue<string>()
            : null;

    /// <summary>The BiDi error a message the mapper could not be handed is answered with, rather than left to time out.</summary>
    private static string? Refusal(JsonObject message)
    {
        if (Id(message) is not { } id || id < 0)
            return null;

        var reason = message["error"] is JsonObject error
            ? error["message"]?.ToString() ?? "refused"
            : (message["result"] as JsonObject)?["exceptionDetails"] is JsonObject details ? Describe(details) : null;
        return reason is null
            ? null
            : new JsonObject { ["type"] = "error", ["id"] = id, ["error"] = "unknown error", ["message"] = reason }.ToJsonString();
    }

    /// <summary>Whether the tab of the mapper crashed or was closed: nothing answers on the channel any more.</summary>
    private bool MapperGone(JsonObject message)
    {
        var parameters = message["params"] as JsonObject;
        return message["method"]?.GetValue<string>() switch
        {
            "Target.detachedFromTarget" => parameters?["sessionId"]?.GetValue<string>() == _sessionId,
            "Inspector.targetCrashed" => message["sessionId"]?.GetValue<string>() == _sessionId,
            _ => false,
        };
    }

    private static string Command(long id, string method, JsonObject parameters, string? sessionId)
    {
        var command = new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters };
        if (!string.IsNullOrEmpty(sessionId))
            command["sessionId"] = sessionId;
        return command.ToJsonString();
    }

    private static long BiDiId(string message) =>
        JsonNode.Parse(message) is JsonObject command && Id(command) is { } id and >= 0
            ? id
            : throw new ArgumentException("A BiDi message carries an id.", nameof(message));

    private static long? Id(JsonObject message) =>
        message["id"] is JsonValue value && long.TryParse(value.ToString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;

    private static string Text(JsonObject result, string key) =>
        result[key]?.GetValue<string>() ?? throw new FirefoxException($"The browser's answer carries no '{key}'.");

    private static string Describe(JsonObject details) =>
        (details["exception"] as JsonObject)?["description"]?.ToString() ?? details["text"]?.ToString() ?? "unknown error";
}
