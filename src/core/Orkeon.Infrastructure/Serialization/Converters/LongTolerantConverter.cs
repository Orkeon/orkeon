using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orkeon.Infrastructure.Serialization.Converters;

/// <summary>JSON converter for <see cref="long"/> that accepts both numeric and string representations using invariant culture.</summary>
public class LongTolerantConverter : JsonConverter<long>
{
    /// <inheritdoc />
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Number => reader.GetInt64(),
            JsonTokenType.String when long.TryParse(reader.GetString(), NumberStyles.Any,
                CultureInfo.InvariantCulture, out var l) => l,
            _ => throw new JsonException($"Cannot convert token type '{reader.TokenType}' to long")
        };
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumberValue(value);
    }
}
