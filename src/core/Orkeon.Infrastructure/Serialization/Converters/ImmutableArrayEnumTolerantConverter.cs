using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orkeon.Infrastructure.Serialization.Converters;

/// <summary>
/// Tolerant converter for <see cref="ImmutableArray{TEnum}"/>. Accepts:
/// scalar string/number coerced to single-element array, JSON array of string/number,
/// a string whose content is a JSON-encoded array (<c>"[\"Calls\"]"</c> — a quirk
/// observed with MiniMax-M2 via the Anthropic-compatible tool-use endpoint),
/// and null/undefined coerced to <see cref="ImmutableArray{TEnum}.Empty"/>.
/// Enum parsing is case-insensitive. Writes as a JSON array of camelCase strings.
/// </summary>
public sealed class ImmutableArrayEnumTolerantConverter<TEnum> : JsonConverter<ImmutableArray<TEnum>>
    where TEnum : struct, Enum
{
    /// <inheritdoc />
    public override ImmutableArray<TEnum> Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return ImmutableArray<TEnum>.Empty;

            case JsonTokenType.String:
            {
                var raw = reader.GetString() ?? string.Empty;
                // LLM quirk: array emitted as a JSON-encoded string, e.g. `"[\"Calls\"]"`.
                if (TryReadJsonEncodedArray(raw, out var fromJson))
                    return fromJson;
                // Normal path: scalar string coerced to a single-element array.
                return ImmutableArray.Create(ParseEnumName(raw));
            }

            case JsonTokenType.Number:
                return ImmutableArray.Create(ParseElement(ref reader));

            case JsonTokenType.StartArray:
            {
                var builder = ImmutableArray.CreateBuilder<TEnum>();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndArray)
                        return builder.ToImmutable();
                    builder.Add(ParseElement(ref reader));
                }
                throw new JsonException($"Unterminated array while reading ImmutableArray<{typeof(TEnum).Name}>.");
            }

            default:
                throw new JsonException(
                    $"Cannot convert token '{reader.TokenType}' to ImmutableArray<{typeof(TEnum).Name}>.");
        }
    }

    private static bool TryReadJsonEncodedArray(string raw, out ImmutableArray<TEnum> result)
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

            var builder = ImmutableArray.CreateBuilder<TEnum>(doc.RootElement.GetArrayLength());
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                builder.Add(item.ValueKind switch
                {
                    JsonValueKind.String => ParseEnumName(item.GetString() ?? string.Empty),
                    JsonValueKind.Number => ParseEnumNumber(item.GetInt64()),
                    _ => throw new JsonException(
                        $"Unsupported element kind '{item.ValueKind}' in JSON-encoded array for {typeof(TEnum).Name}."),
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

    private static TEnum ParseEnumName(string raw)
    {
        if (Enum.TryParse<TEnum>(raw, ignoreCase: true, out var parsed))
            return parsed;
        throw new JsonException($"'{raw}' is not a valid {typeof(TEnum).Name} value.");
    }

    private static TEnum ParseEnumNumber(long value)
    {
        if (Enum.IsDefined(typeof(TEnum), (int)value))
            return (TEnum)Enum.ToObject(typeof(TEnum), value);
        throw new JsonException($"Numeric value '{value}' is not a valid {typeof(TEnum).Name} member.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ImmutableArray<TEnum> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStartArray();
        foreach (var item in value)
        {
            var name = Enum.GetName<TEnum>(item) ?? item.ToString();
            writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(name));
        }
        writer.WriteEndArray();
    }

    private static TEnum ParseElement(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var raw = reader.GetString()
                ?? throw new JsonException($"Null string while parsing {typeof(TEnum).Name} element.");
            if (Enum.TryParse<TEnum>(raw, ignoreCase: true, out var parsed))
                return parsed;
            throw new JsonException($"'{raw}' is not a valid {typeof(TEnum).Name} value.");
        }

        if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetInt64(out var n) && Enum.IsDefined(typeof(TEnum), (int)n))
                return (TEnum)Enum.ToObject(typeof(TEnum), n);
            throw new JsonException($"Numeric value is not a valid {typeof(TEnum).Name} member.");
        }

        throw new JsonException(
            $"Cannot parse token '{reader.TokenType}' as {typeof(TEnum).Name}.");
    }
}
