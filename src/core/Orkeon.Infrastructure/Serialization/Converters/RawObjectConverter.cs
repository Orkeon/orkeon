using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orkeon.Infrastructure.Serialization.Converters;

/// <summary>
/// Converts JSON values to raw .NET types when the target type is object.
/// Without this, JsonSerializer produces JsonElement values inside Dictionary&lt;string, object&gt;.
/// </summary>
public class RawObjectConverter : JsonConverter<object>
{
    /// <inheritdoc />
    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number when reader.TryGetInt32(out var i) => i,
            JsonTokenType.Number when reader.TryGetInt64(out var l) => l,
            JsonTokenType.Number when reader.TryGetDecimal(out var d) => d,
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Null => null,
            JsonTokenType.StartArray => ReadArray(ref reader, options),
            JsonTokenType.StartObject => ReadObject(ref reader, options),
            _ => throw new JsonException($"Unexpected token: {reader.TokenType}")
        };
    }

    private static List<object> ReadArray(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var list = new List<object>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            var converter = (RawObjectConverter)options.GetConverter(typeof(object));
            var value = converter.Read(ref reader, typeof(object), options);
            if (value is not null) list.Add(value);
        }
        return list;
    }

    private static Dictionary<string, object> ReadObject(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var dict = new Dictionary<string, object>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var key = reader.GetString()!;
            reader.Read();
            var converter = (RawObjectConverter)options.GetConverter(typeof(object));
            var value = converter.Read(ref reader, typeof(object), options);
            if (value is not null) dict[key] = value;
        }
        return dict;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (value is null) { writer.WriteNullValue(); return; }
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
