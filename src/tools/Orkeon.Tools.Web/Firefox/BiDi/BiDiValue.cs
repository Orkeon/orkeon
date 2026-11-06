using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orkeon.Tools.Web.Firefox.BiDi;

/// <summary>
/// Reads the values a script hands back. WebDriver BiDi describes each one
/// (<c>{"type":"string","value":"a"}</c>, <c>{"type":"array","value":[...]}</c>); this turns the
/// description back into the plain JSON an agent reads.
/// </summary>
internal static class BiDiValue
{
    // An agent reads the value: letters with accents and signs stay as they are, not as \uXXXX.
    private static readonly JsonSerializerOptions s_readable = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The value as text: a string as it is, anything else as compact JSON.</summary>
    public static string Render(JsonNode? remote)
    {
        if (remote is not JsonObject described)
            return string.Empty;

        var type = TypeOf(described);
        if (type == "undefined")
            return "undefined";
        if (type == "string")
            return described["value"]?.GetValue<string>() ?? string.Empty;

        return ToJson(described)?.ToJsonString(s_readable) ?? "null";
    }

    /// <summary>The plain JSON of a described value; what JSON cannot say becomes a short text.</summary>
    public static JsonNode? ToJson(JsonNode? remote)
    {
        if (remote is not JsonObject described)
            return null;

        var type = TypeOf(described);
        var value = described["value"];
        switch (type)
        {
            case "undefined" or "null":
                return null;
            case "string" or "boolean" or "number" or "bigint" or "date":
                // A number JSON cannot say (NaN, Infinity, -0) comes as a string and stays one.
                return value?.DeepClone();
            case "array" or "set" or "nodelist" or "htmlcollection":
                return value is JsonArray items ? new JsonArray([.. items.Select(ToJson)]) : $"[{type}]";
            case "object" or "map":
                if (value is not JsonArray entries)
                    return $"[{type}]";

                var result = new JsonObject();
                foreach (var entry in entries.OfType<JsonArray>().Where(entry => entry.Count == 2))
                {
                    // A key is a plain string, or a described value when it is not one (a map).
                    var key = entry[0] is JsonValue plain && plain.TryGetValue<string>(out var text) ? text : Render(entry[0]);
                    result[key] = ToJson(entry[1]);
                }

                return result;
            case "regexp":
                return $"/{value?["pattern"]?.GetValue<string>()}/{value?["flags"]?.GetValue<string>()}";
            default:
                return $"[{type}]";
        }
    }

    private static string TypeOf(JsonObject described) =>
        described["type"] is JsonValue type && type.TryGetValue<string>(out var name) ? name : string.Empty;
}
