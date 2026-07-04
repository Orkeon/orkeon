using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orkeon.Infrastructure.Serialization.Converters;

/// <summary>JSON converter for <see cref="int"/> that accepts both numeric and string representations using invariant culture.</summary>
public class IntTolerantConverter : JsonConverter<int>
{
    /// <inheritdoc />
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Number => reader.GetInt32(),
            JsonTokenType.String when int.TryParse(reader.GetString(), NumberStyles.Any,
                CultureInfo.InvariantCulture, out var i) => i,
            _ => throw new JsonException($"Cannot convert token type '{reader.TokenType}' to int")
        };
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumberValue(value);
    }
}
