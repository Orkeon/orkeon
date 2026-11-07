using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orkeon.Infrastructure.Serialization.Converters;

/// <summary>
/// The wire form of a <see cref="FlagsAttribute"/> enum: each member's name in snake_case lower
/// (<c>project_depends</c>, <c>public_exports</c>), a combination as its names joined by <c>", "</c>
/// (<c>imports, calls</c>). A value that is itself a named member (<c>All</c>, <c>None</c>) keeps that name.
/// </summary>
/// <remarks>
/// Reading is tolerant: case, underscores, hyphens and spaces inside a name are ignored, the names of a
/// combination are separated by <c>,</c> or <c>|</c>. A number is refused: the wire carries names only.
/// </remarks>
internal static class FlagsEnumNames<TEnum>
    where TEnum : struct, Enum
{
    private static readonly bool IsUnsigned64 = Type.GetTypeCode(typeof(TEnum)) == TypeCode.UInt64;

    // Every named value, first declared name wins (an alias keeps the earlier name on the wire).
    private static readonly Dictionary<ulong, string> NameByBits = BuildNameByBits();

    // The single-bit members, ascending, to spell out a combination.
    private static readonly (ulong Bits, string Name)[] SingleBits = [.. NameByBits
        .Where(p => p.Key != 0 && (p.Key & (p.Key - 1)) == 0)
        .OrderBy(p => p.Key)
        .Select(p => (p.Key, p.Value))];

    private static readonly Dictionary<string, ulong> BitsByAlias = BuildAliases();

    /// <summary>The wire names, in declaration order, for error messages.</summary>
    public static string ValidNames { get; } = string.Join(", ", Enum.GetNames<TEnum>().Select(ToWireName).Distinct(StringComparer.Ordinal));

    public static string Format(TEnum value)
    {
        var bits = ToBits(value);
        if (NameByBits.TryGetValue(bits, out var exact)) return exact;

        var parts = new List<string>();
        foreach (var (single, name) in SingleBits)
        {
            if ((bits & single) == 0) continue;
            parts.Add(name);
            bits &= ~single;
        }

        // Bits no member names stay visible as a number rather than being dropped.
        if (bits != 0) parts.Add(bits.ToString(CultureInfo.InvariantCulture));
        return string.Join(", ", parts);
    }

    public static bool TryParse(string raw, out TEnum value)
    {
        value = default;
        var bits = 0UL;
        foreach (var token in raw.Split([',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (BitsByAlias.TryGetValue(Normalize(token), out var named))
                bits |= named;
            else
                return false;
        }

        value = FromBits(bits);
        return true;
    }

    public static JsonException InvalidValue(string raw) =>
        new($"Invalid value '{raw}' for {typeof(TEnum).Name}. Valid values: {ValidNames}; combine them with ', '.");

    private static string ToWireName(string memberName) => JsonNamingPolicy.SnakeCaseLower.ConvertName(memberName);

    private static Dictionary<ulong, string> BuildNameByBits()
    {
        var map = new Dictionary<ulong, string>();
        foreach (var name in Enum.GetNames<TEnum>())
            map.TryAdd(ToBits(Enum.Parse<TEnum>(name)), ToWireName(name));
        return map;
    }

    private static Dictionary<string, ulong> BuildAliases()
    {
        var map = new Dictionary<string, ulong>(StringComparer.Ordinal);
        foreach (var name in Enum.GetNames<TEnum>())
            map.TryAdd(Normalize(name), ToBits(Enum.Parse<TEnum>(name)));
        return map;
    }

    private static string Normalize(string input)
    {
        Span<char> buffer = input.Length <= 64 ? stackalloc char[input.Length] : new char[input.Length];
        var w = 0;
        foreach (var c in input)
        {
            if (c is '_' or '-' or ' ') continue;
            buffer[w++] = char.ToLowerInvariant(c);
        }
        return new string(buffer[..w]);
    }

    private static ulong ToBits(TEnum value) => IsUnsigned64
        ? Convert.ToUInt64(value, CultureInfo.InvariantCulture)
        : unchecked((ulong)Convert.ToInt64(value, CultureInfo.InvariantCulture));

    private static TEnum FromBits(ulong bits) => (TEnum)Enum.ToObject(typeof(TEnum), bits);
}

/// <summary>
/// String converter for a scalar <see cref="FlagsAttribute"/> enum, in the form <see cref="FlagsEnumNames{TEnum}"/>
/// gives. Reads a string, or an array of names (their union).
/// </summary>
internal sealed class TolerantFlagsEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    /// <inheritdoc />
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return ReadOne(ref reader);

            case JsonTokenType.StartArray:
            {
                var union = 0L;
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndArray)
                        return (TEnum)Enum.ToObject(typeof(TEnum), union);
                    union |= Convert.ToInt64(ReadOne(ref reader), CultureInfo.InvariantCulture);
                }
                throw new JsonException($"Unterminated array while reading {typeof(TEnum).Name}.");
            }

            default:
                throw new JsonException($"Cannot convert token '{reader.TokenType}' to {typeof(TEnum).Name}. Valid values: {FlagsEnumNames<TEnum>.ValidNames}.");
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(FlagsEnumNames<TEnum>.Format(value));
    }

    private static TEnum ReadOne(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var raw = reader.GetString() ?? string.Empty;
            return FlagsEnumNames<TEnum>.TryParse(raw, out var parsed) ? parsed : throw FlagsEnumNames<TEnum>.InvalidValue(raw);
        }

        throw new JsonException($"Cannot convert token '{reader.TokenType}' to {typeof(TEnum).Name}. Valid values: {FlagsEnumNames<TEnum>.ValidNames}.");
    }
}
