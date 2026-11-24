using System.Globalization;
using System.Text.Json;

namespace Orkeon.Tools.Chat.Providers.Teams;

/// <summary>Reads of Graph's JSON that never throw on a missing or unexpected member.</summary>
internal static class GraphJson
{
    private const int MaxErrorLength = 300;

    /// <summary>The string member <paramref name="name"/>, or null.</summary>
    public static string? Text(JsonElement element, string name) =>
        Member(element, name) is { } value ? Text(value) : null;

    /// <summary>The string <paramref name="value"/> is, or null: JSON may escape half a character, which is no text.</summary>
    public static string? Text(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
            return null;
        try
        {
            return value.GetString();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The code of the inner error of a failure's body, or null.</summary>
    public static string? InnerErrorCode(string body)
    {
        try
        {
            var json = JsonElement.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            return Object(json, "error") is { } error && Object(error, "innerError") is { } inner ? Text(inner, "code") : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The object member <paramref name="name"/>, or null.</summary>
    public static JsonElement? Object(JsonElement element, string name) =>
        Member(element, name) is { ValueKind: JsonValueKind.Object } value ? value : null;

    /// <summary>The items of the array member <paramref name="name"/> that are objects.</summary>
    public static IEnumerable<JsonElement> Items(JsonElement element, string name = "value") =>
        Member(element, name) is { ValueKind: JsonValueKind.Array } array
            ? array.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object)
            : [];

    /// <summary>The date member <paramref name="name"/>, or null.</summary>
    public static DateTimeOffset? Date(JsonElement element, string name) =>
        DateTimeOffset.TryParse(Text(element, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;

    /// <summary>The code and message of an error body, shortened and on one line.</summary>
    public static string Error(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var detail = body;
        try
        {
            if (Object(JsonElement.Parse(body), "error") is { } error)
                detail = $"{Text(error, "code")}: {Text(error, "message")}";
        }
        catch (JsonException)
        {
            // Not JSON: the raw text is shown.
        }

        detail = ChatToolHelpers.OneLine(detail).Trim(' ', ':');
        if (detail.Length == 0)
            return "no detail";
        return detail.Length > MaxErrorLength ? detail[..MaxErrorLength] : detail;
    }

    private static JsonElement? Member(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : null;
}
