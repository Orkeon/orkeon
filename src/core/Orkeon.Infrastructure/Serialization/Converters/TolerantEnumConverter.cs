using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orkeon.Infrastructure.Serialization.Converters;

/// <summary>
/// Factory that installs a <see cref="TolerantEnumConverter{TEnum}"/> for every
/// non-flags enum encountered during deserialization, unless a more specific
/// converter is already registered ahead of this one in the options.
/// </summary>
/// <remarks>
/// Replaces the default <see cref="JsonStringEnumConverter"/> with one that:
/// <list type="bullet">
///   <item><description>Accepts case-insensitive names (<c>l1_package</c>
///     matches <c>L1_Package</c>)</description></item>
///   <item><description>Accepts underscore-stripped variants (<c>L1Package</c>
///     matches <c>L1_Package</c>, so camelCase-serialised output round-trips)</description></item>
///   <item><description>On an unknown value, throws a <see cref="JsonException"/>
///     listing the valid enum names — the returned error surface gives an
///     LLM (or a developer) the signal needed to self-correct rather than
///     guessing again.</description></item>
/// </list>
/// Write path uses camelCase to stay compatible with existing tool output
/// (<c>"l1_Package"</c> is what previous rounds emitted).
/// </remarks>
public sealed class TolerantEnumConverterFactory : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        if (!typeToConvert.IsEnum)
            return false;
        // Leave [Flags] enums to the more specific Immutable array / flags
        // converters already registered; flags parsing with comma-separated
        // strings requires different tolerance semantics.
        return typeToConvert.GetCustomAttributes(typeof(FlagsAttribute), inherit: false).Length == 0;
    }

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var converterType = typeof(TolerantEnumConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}

/// <summary>
/// Tolerant string↔enum converter. See <see cref="TolerantEnumConverterFactory"/>
/// for the behaviour contract.
/// </summary>
public sealed class TolerantEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    // Pre-computed map: lowercase + underscores-stripped → exact enum name.
    // Cached per-TEnum so the case/underscore lookup is O(1) on a hash.
    private static readonly Dictionary<string, TEnum> Aliases = BuildAliasMap();

    // Ordered list of valid names for the error message — preserves declaration
    // order, which is how developers expect to read them.
    private static readonly string[] ValidNames = Enum.GetNames<TEnum>();

    private static Dictionary<string, TEnum> BuildAliasMap()
    {
        var map = new Dictionary<string, TEnum>(StringComparer.Ordinal);
        foreach (var name in Enum.GetNames<TEnum>())
        {
            var value = Enum.Parse<TEnum>(name);
            Register(map, name, value);
        }
        return map;
    }

    private static void Register(Dictionary<string, TEnum> map, string name, TEnum value)
    {
        map.TryAdd(Normalize(name), value);
    }

    private static string Normalize(string input)
    {
        // Lowercase + strip underscores — covers the common LLM variations
        // (L1_Package / l1_package / L1Package / l1package all normalise to
        // "l1package") without requiring a per-enum alias table.
        Span<char> buffer = input.Length <= 64 ? stackalloc char[input.Length] : new char[input.Length];
        var w = 0;
        foreach (var c in input)
        {
            if (c == '_') continue;
            buffer[w++] = char.ToLowerInvariant(c);
        }
        return new string(buffer[..w]);
    }

    /// <inheritdoc />
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var raw = reader.GetString() ?? string.Empty;
            if (Aliases.TryGetValue(Normalize(raw), out var parsed))
                return parsed;
            throw InvalidValueException(raw);
        }

        if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetInt64(out var n) && Enum.IsDefined(typeof(TEnum), (int)n))
                return (TEnum)Enum.ToObject(typeof(TEnum), n);
            throw new JsonException(
                $"Numeric value is not a valid {typeof(TEnum).Name} member. Valid values: {string.Join(", ", ValidNames)}.");
        }

        throw new JsonException(
            $"Cannot convert token '{reader.TokenType}' to {typeof(TEnum).Name}.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var name = Enum.GetName<TEnum>(value) ?? value.ToString();
        writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(name));
    }

    private static JsonException InvalidValueException(string raw)
    {
        // Error message intentionally shaped for LLM consumption — it names the
        // enum, the offending value, and enumerates every valid option so the
        // model's next attempt can pick one deterministically instead of
        // hallucinating a different wrong one.
        return new JsonException(
            $"Invalid value '{raw}' for {typeof(TEnum).Name}. Valid values: {string.Join(", ", ValidNames)}.");
    }
}
