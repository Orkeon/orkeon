using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Orkeon.Tools.Web.Tests.Doubles;

/// <summary>A safaridriver of one tab, answering in memory.</summary>
internal sealed class FakeSafariDriver : HttpMessageHandler
{
    public List<string> Calls { get; } = [];

    public List<JsonObject> Scripts { get; } = [];

    public List<JsonObject> Actions { get; } = [];

    public List<string> Handles { get; } = ["one"];

    public string? Dialog { get; set; }

    /// <summary>The command refused, and the WebDriver error it is refused with.</summary>
    public Dictionary<string, string> Refused { get; } = [];

    /// <summary>What the next scripts return; <c>null</c> once used up.</summary>
    public Queue<JsonNode?> Results { get; } = new();

    /// <summary>What the script that watches the page returns.</summary>
    public JsonNode? Watch { get; set; } = new JsonArray(false, "complete");

    /// <summary>What an action of the page does.</summary>
    public Action? OnActions { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? []
            : JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken)) as JsonObject ?? [];
        var path = request.RequestUri!.AbsolutePath;
        var command = path.StartsWith("/session/s1/", StringComparison.Ordinal) ? path["/session/s1/".Length..] : path;
        lock (Calls)
            Calls.Add($"{request.Method} {command}");
        if (Refused.TryGetValue(command, out var error))
            return Answer(HttpStatusCode.InternalServerError, new JsonObject { ["error"] = error, ["message"] = "refused" });

        JsonNode? value = null;
        switch (command)
        {
            case "/status":
                value = new JsonObject { ["ready"] = true };
                break;
            case "/session":
                value = new JsonObject { ["sessionId"] = "s1", ["capabilities"] = new JsonObject() };
                break;
            case "window/handles":
                value = new JsonArray([.. Handles.Select(handle => JsonValue.Create(handle))]);
                break;
            case "url":
                value = "http://site/";
                break;
            case "screenshot":
                value = "cG5n";
                break;
            case "alert/text":
                if (Dialog is null)
                    return Answer(HttpStatusCode.NotFound, new JsonObject { ["error"] = "no such alert", ["message"] = "" });
                value = Dialog;
                break;
            case "alert/dismiss":
                Dialog = null;
                break;
            case "actions" when request.Method == HttpMethod.Post:
                Actions.Add(body);
                OnActions?.Invoke();
                break;
            case "execute/sync" when body["script"]!.GetValue<string>().StartsWith("return [window[", StringComparison.Ordinal):
                value = Watch?.DeepClone();
                break;
            case "execute/sync" when body["script"]!.GetValue<string>().StartsWith("const key", StringComparison.Ordinal):
                break;
            case "execute/sync" or "execute/async":
                Scripts.Add(body);
                value = Results.Count > 0 ? Results.Dequeue() : null;
                break;
        }

        return Answer(HttpStatusCode.OK, value);
    }

    private static HttpResponseMessage Answer(HttpStatusCode status, JsonNode? value) => new(status)
    {
        Content = new StringContent(new JsonObject { ["value"] = value }.ToJsonString(), Encoding.UTF8, "application/json"),
    };
}
