using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orkeon.Infrastructure.Serialization.Converters;

/// <summary>JSON converter for <see cref="decimal"/> that accepts both numeric and string representations using invariant culture.</summary>
public class DecimalInvariantConverter : JsonConverter<decimal>
{
    /// <inheritdoc />
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Number => reader.GetDecimal(),
            JsonTokenType.String when decimal.TryParse(reader.GetString(), NumberStyles.Any,
                CultureInfo.InvariantCulture, out var d) => d,
            _ => throw new JsonException($"Cannot convert token type '{reader.TokenType}' to decimal")
        };
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumberValue(value);
    }
}
