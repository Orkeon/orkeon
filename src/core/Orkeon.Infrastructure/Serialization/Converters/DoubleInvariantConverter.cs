using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orkeon.Infrastructure.Serialization.Converters;

/// <summary>JSON converter for <see cref="double"/> that accepts both numeric and string representations using invariant culture.</summary>
public class DoubleInvariantConverter : JsonConverter<double>
{
    /// <inheritdoc />
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Number => reader.GetDouble(),
            JsonTokenType.String when double.TryParse(reader.GetString(), NumberStyles.Any,
                CultureInfo.InvariantCulture, out var d) => d,
            _ => throw new JsonException($"Cannot convert token type '{reader.TokenType}' to double")
        };
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumberValue(value);
    }
}
