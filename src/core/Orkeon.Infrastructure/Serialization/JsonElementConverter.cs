using System.Text.Json;

namespace Orkeon.Infrastructure.Serialization;

/// <summary>
/// Converts <see cref="JsonElement"/> values to .NET primitive types.
/// Used to unwrap <c>System.Text.Json</c> deserialization results (which produce
/// <see cref="JsonElement"/> wrappers) into native types (<c>string</c>, <c>long</c>,
/// <c>double</c>, <c>bool</c>, etc.) that downstream validators expect.
/// </summary>
public static class JsonElementConverter
{
    /// <summary>
    /// Recursively converts a <see cref="JsonElement"/> of <see cref="JsonValueKind.Object"/>
    /// into a <see cref="Dictionary{String, Object}"/> with unwrapped .NET primitive values.
    /// </summary>
    /// <param name="element">The JSON element to convert. Non-object elements return an empty dictionary.</param>
    /// <returns>A dictionary of property name to .NET primitive value pairs.</returns>
    public static Dictionary<string, object?> ToDict(JsonElement element)
    {
        var dict = new Dictionary<string, object?>();
        if (element.ValueKind != JsonValueKind.Object) return dict;

        foreach (var prop in element.EnumerateObject())
            dict[prop.Name] = ConvertElement(prop.Value);

        return dict;
    }

    /// <summary>
    /// Converts a single <see cref="JsonElement"/> to its closest .NET primitive equivalent.
    /// </summary>
    /// <param name="element">The JSON element to convert.</param>
    /// <returns>
    /// <c>string</c> for strings, <c>long</c> or <c>double</c> for numbers,
    /// <c>bool</c> for booleans, <see cref="List{Object}"/> for arrays,
    /// <see cref="Dictionary{String, Object}"/> for objects, or <c>null</c>.
    /// </returns>
    public static object? ConvertElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? (object)l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => ConvertArray(element),
        JsonValueKind.Object => ToDict(element),
        _ => null
    };

    /// <summary>
    /// Converts a <see cref="JsonElement"/> of <see cref="JsonValueKind.Array"/>
    /// into a <see cref="List{Object}"/> with unwrapped .NET primitive values.
    /// </summary>
    /// <param name="element">The JSON array element to convert.</param>
    /// <returns>A list of converted .NET primitive values (nulls are preserved).</returns>
    public static IReadOnlyList<object?> ConvertArray(JsonElement element)
    {
        var list = new List<object?>();
        foreach (var item in element.EnumerateArray())
            list.Add(ConvertElement(item));
        return list;
    }
}
