using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orkeon.Infrastructure.Serialization.Converters;

/// <summary>
/// Tolerant URI converter for tool wire contracts. URL parameters arrive from the
/// LLM (and YAML defaults) as plain strings; this maps them to <see cref="Uri"/>.
/// <para>
/// Unlike the built-in converter it is forgiving on the two cases that matter for
/// agent-supplied input:
/// <list type="bullet">
///   <item>empty / whitespace-only strings map to <c>null</c> (so a tool's own
///   "URL cannot be empty" validation reports a friendly error instead of the
///   pipeline throwing an opaque JSON deserialization failure);</item>
///   <item>relative or partial values (e.g. a host substring used as a filter) are
///   accepted via <see cref="UriKind.RelativeOrAbsolute"/> rather than rejected.</item>
/// </list>
/// </para>
/// </summary>
public class UriTolerantConverter : JsonConverter<Uri?>
{
    /// <inheritdoc />
    public override Uri? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                var s = reader.GetString();
                if (string.IsNullOrWhiteSpace(s))
                    return null;
                if (Uri.TryCreate(s, UriKind.RelativeOrAbsolute, out var uri))
                    return uri;
                throw new JsonException($"Cannot convert string '{s}' to a URI.");
            default:
                throw new JsonException($"Cannot convert token type '{reader.TokenType}' to a URI.");
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Uri? value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value.OriginalString);
    }
}
