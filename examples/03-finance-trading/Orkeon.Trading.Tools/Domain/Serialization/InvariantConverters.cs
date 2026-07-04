using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orkeon.Trading.Tools.Domain.Serialization;

/// <summary>
/// JSON converter for decimal with InvariantCulture and zero compression
/// </summary>
public class DecimalInvariantConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return reader.GetDecimal();
        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            if (decimal.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                return value;
        }
        return 0m;
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
    {
        var str = value.ToString("0.##################", CultureInfo.InvariantCulture);
        writer.WriteRawValue(str);
    }
}

/// <summary>
/// JSON converter for double with InvariantCulture and zero compression
/// </summary>
public class DoubleInvariantConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return reader.GetDouble();
        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            if (double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                return value;
        }
        return 0.0;
    }

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
    {
        var str = value.ToString("0.##################", CultureInfo.InvariantCulture);
        writer.WriteRawValue(str);
    }
}
