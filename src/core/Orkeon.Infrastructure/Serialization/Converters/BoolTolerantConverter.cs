using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orkeon.Infrastructure.Serialization.Converters;

/// <summary>
/// Tolerant boolean converter: accepts JSON booleans, strings ("true"/"false"),
/// and numeric values (0/non-zero). Handles YAML defaults that arrive as strings.
/// </summary>
public class BoolTolerantConverter : JsonConverter<bool>
{
    /// <inheritdoc />
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.String when bool.TryParse(reader.GetString(), out var b) => b,
            JsonTokenType.String when reader.GetString() is "1" => true,
            JsonTokenType.String when reader.GetString() is "0" => false,
            JsonTokenType.Number => reader.TryGetInt64(out var n) ? n != 0 : reader.GetDouble() != 0,
            _ => throw new JsonException($"Cannot convert token type '{reader.TokenType}' to boolean")
        };
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteBooleanValue(value);
    }
}
