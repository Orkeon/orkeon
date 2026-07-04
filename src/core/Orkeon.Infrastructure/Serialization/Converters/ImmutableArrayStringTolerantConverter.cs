using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orkeon.Infrastructure.Serialization.Converters;

/// <summary>
/// Tolerant converter for <see cref="ImmutableArray{String}"/>. Accepts:
/// a JSON array of strings, a scalar string coerced to a single-element array,
/// a string whose content is a JSON-encoded array (<c>"[\"a\",\"b\"]"</c> — a
/// quirk observed with MiniMax-M2 via the Anthropic-compatible tool-use
/// endpoint for params such as <c>sub_graph.seeds</c>), and null/undefined
/// coerced to <see cref="ImmutableArray{String}.Empty"/>.
/// </summary>
public sealed class ImmutableArrayStringTolerantConverter : JsonConverter<ImmutableArray<string>>
{
    /// <inheritdoc />
    public override ImmutableArray<string> Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return ImmutableArray<string>.Empty;

            case JsonTokenType.String:
            {
                var raw = reader.GetString() ?? string.Empty;
                if (TryReadJsonEncodedArray(raw, out var fromJson))
                    return fromJson;
                // Scalar coerced to a single-element array.
                return ImmutableArray.Create(raw);
            }

            case JsonTokenType.StartArray:
            {
                var builder = ImmutableArray.CreateBuilder<string>();
                while (reader.Read())
                {
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.EndArray:
                            return builder.ToImmutable();
                        case JsonTokenType.String:
                            builder.Add(reader.GetString() ?? string.Empty);
                            break;
                        case JsonTokenType.Null:
                            // Skip nulls rather than failing — parity with scalar coercion.
                            break;
                        default:
                            throw new JsonException(
                                $"Cannot convert token '{reader.TokenType}' to string element in ImmutableArray<string>.");
                    }
                }
                throw new JsonException("Unterminated array while reading ImmutableArray<string>.");
            }

            default:
                throw new JsonException(
                    $"Cannot convert token '{reader.TokenType}' to ImmutableArray<string>.");
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ImmutableArray<string> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStartArray();
        foreach (var item in value)
            writer.WriteStringValue(item);
        writer.WriteEndArray();
    }

    private static bool TryReadJsonEncodedArray(string raw, out ImmutableArray<string> result)
    {
        result = default;
        var span = raw.AsSpan().Trim();
        if (span.Length < 2 || span[0] != '[' || span[span.Length - 1] != ']')
            return false;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return false;

            var builder = ImmutableArray.CreateBuilder<string>(doc.RootElement.GetArrayLength());
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                builder.Add(item.ValueKind switch
                {
                    JsonValueKind.String => item.GetString() ?? string.Empty,
                    JsonValueKind.Null => string.Empty,
                    _ => throw new JsonException(
                        $"Unsupported element kind '{item.ValueKind}' in JSON-encoded array for ImmutableArray<string>."),
                });
            }

            result = builder.ToImmutable();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
