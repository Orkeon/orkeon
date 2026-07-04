using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orkeon.Analysis.Core.Serialization;

internal sealed class EmbeddingJsonConverter : JsonConverter<ReadOnlyMemory<float>?>
{
    public override ReadOnlyMemory<float>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Embedding must be a base64 string or null.");

        var base64 = reader.GetString();
        if (string.IsNullOrEmpty(base64)) return null;

        var bytes = Convert.FromBase64String(base64);
        if (bytes.Length % sizeof(float) != 0)
            throw new JsonException("Embedding base64 payload length must be a multiple of 4.");

        var floats = new float[bytes.Length / sizeof(float)];
        for (var i = 0; i < floats.Length; i++)
        {
            floats[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * sizeof(float), sizeof(float)));
        }
        return floats;
    }

    public override void Write(Utf8JsonWriter writer, ReadOnlyMemory<float>? value, JsonSerializerOptions options)
    {
        if (value is null || value.Value.Length == 0)
        {
            writer.WriteNullValue();
            return;
        }

        var span = value.Value.Span;
        var bytes = new byte[span.Length * sizeof(float)];
        for (var i = 0; i < span.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * sizeof(float), sizeof(float)), span[i]);
        }
        writer.WriteStringValue(Convert.ToBase64String(bytes));
    }
}
