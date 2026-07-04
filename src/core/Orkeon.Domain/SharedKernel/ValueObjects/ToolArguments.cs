using System.Collections.Immutable;
using System.Globalization;
using Orkeon.Generators;

namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// Strongly typed tool arguments container with type-safe accessors.
/// </summary>
[TypedDictionary(typeof(ToolArgumentValue), CacheEmpty = true, EmitGenericAdd = false)]
public sealed partial record ToolArguments
{
    /// <summary>
    /// Gets a required value of type T.
    /// </summary>
    public T GetRequired<T>(string name) where T : struct
    {
        if (!_items.TryGetValue(name, out var value))
            throw new ArgumentException($"Required argument '{name}' not found", nameof(name));

        return value.GetValue<T>();
    }

    /// <summary>
    /// Gets an optional value of type T.
    /// </summary>
    public T? GetOptional<T>(string name) where T : struct
    {
        if (!_items.TryGetValue(name, out var value))
            return null;

        return value.GetValue<T>();
    }

    /// <summary>
    /// Gets a required string value.
    /// </summary>
    public string GetRequiredString(string name)
    {
        if (!_items.TryGetValue(name, out var value))
            throw new ArgumentException($"Required argument '{name}' not found", nameof(name));

        return value.GetStringValue();
    }

    /// <summary>
    /// Gets an optional string value.
    /// </summary>
    public string? GetOptionalString(string name)
    {
        if (!_items.TryGetValue(name, out var value))
            return null;

        return value.GetStringValue();
    }

    /// <summary>
    /// Gets a required object value.
    /// </summary>
    public TObject GetRequiredObject<TObject>(string name) where TObject : class
    {
        if (!_items.TryGetValue(name, out var value))
            throw new ArgumentException($"Required argument '{name}' not found", nameof(name));

        return value.GetObjectValue<TObject>();
    }

    /// <summary>
    /// Gets an optional object value.
    /// </summary>
    public TObject? GetOptionalObject<TObject>(string name) where TObject : class
    {
        if (!_items.TryGetValue(name, out var value))
            return null;

        return value.TryGetObjectValue<TObject>();
    }

    /// <summary>
    /// Checks if an argument exists.
    /// </summary>
    public bool Contains(string name) => _items.ContainsKey(name);

    /// <summary>
    /// Gets all argument names.
    /// </summary>
    public IEnumerable<string> Names => _items.Keys;

    /// <summary>
    /// Gets the count of arguments.
    /// </summary>
    public int Count => _items.Count;

    /// <summary>
    /// Converts to dictionary for JSON serialization only.
    /// </summary>
    public Dictionary<string, object> ToDictionary()
    {
        return _items.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.RawValue);
    }

    /// <inheritdoc />
    public bool Equals(ToolArguments? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (_items.Count != other._items.Count) return false;

        foreach (var kvp in _items)
        {
            if (!other._items.TryGetValue(kvp.Key, out var otherValue))
                return false;
            if (!kvp.Value.Equals(otherValue))
                return false;
        }

        return true;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var kvp in _items.OrderBy(x => x.Key))
        {
            hash.Add(kvp.Key);
            hash.Add(kvp.Value);
        }
        return hash.ToHashCode();
    }

    /// <summary>Builder for constructing <see cref="ToolArguments"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Adds a string argument.
        /// </summary>
        [DictionaryEntry(Factory = "FromString")]
        public partial Builder AddString(string name, string value);

        /// <summary>
        /// Adds an integer argument.
        /// </summary>
        [DictionaryEntry(Factory = "FromInt")]
        public partial Builder AddInt(string name, int value);

        /// <summary>
        /// Adds a boolean argument.
        /// </summary>
        [DictionaryEntry(Factory = "FromBool")]
        public partial Builder AddBool(string name, bool value);

        /// <summary>
        /// Adds a double argument.
        /// </summary>
        [DictionaryEntry(Factory = "FromDouble")]
        public partial Builder AddDouble(string name, double value);

        /// <summary>
        /// Adds an object argument.
        /// </summary>
        [DictionaryEntry(Factory = "FromObject")]
        public partial Builder AddObject<T>(string name, T value) where T : class;
    }
}

/// <summary>
/// Represents a single tool argument value with type information.
/// </summary>
public sealed class ToolArgumentValue
{
    private readonly object _value;
    private readonly Type _type;

    private ToolArgumentValue(object value, Type type)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
        ArgumentNullException.ThrowIfNull(type);
        _type = type;
    }

    /// <summary>
    /// Gets the raw value.
    /// </summary>
    public object RawValue => _value;

    /// <summary>
    /// Gets the value type.
    /// </summary>
    public Type ValueType => _type;

    /// <summary>
    /// Creates from a string value.
    /// </summary>
    public static ToolArgumentValue FromString(string value) =>
        new(value, typeof(string));

    /// <summary>
    /// Creates from an integer value.
    /// </summary>
    public static ToolArgumentValue FromInt(int value) =>
        new(value, typeof(int));

    /// <summary>
    /// Creates from a boolean value.
    /// </summary>
    public static ToolArgumentValue FromBool(bool value) =>
        new(value, typeof(bool));

    /// <summary>
    /// Creates from a double value.
    /// </summary>
    public static ToolArgumentValue FromDouble(double value) =>
        new(value, typeof(double));

    /// <summary>
    /// Creates from an object value.
    /// </summary>
    public static ToolArgumentValue FromObject<T>(T value) where T : class =>
        new(value, typeof(T));


    /// <summary>
    /// Gets the value as type T.
    /// </summary>
    public T GetValue<T>() where T : struct
    {
        if (_value is T typedValue)
            return typedValue;

        try
        {
            return (T)Convert.ChangeType(_value, typeof(T), CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            throw new InvalidCastException(
                $"Cannot convert value of type {_type.Name} to {typeof(T).Name}", ex);
        }
    }

    /// <summary>
    /// Gets the string value.
    /// </summary>
    public string GetStringValue()
    {
        if (_value is string str)
            return str;

        // Use InvariantCulture for numeric types to ensure consistent formatting
        return _value switch
        {
            float f => f.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString(CultureInfo.InvariantCulture),
            decimal dec => dec.ToString(CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => _value.ToString() ?? string.Empty
        };
    }

    /// <summary>
    /// Gets the object value as type T.
    /// </summary>
    public T GetObjectValue<T>() where T : class
    {
        if (_value is T typedValue)
            return typedValue;

        throw new InvalidCastException(
            $"Cannot convert value of type {_type.Name} to {typeof(T).Name}");
    }

    /// <summary>
    /// Tries to get the object value as type T.
    /// </summary>
    public T? TryGetObjectValue<T>() where T : class
    {
        return _value as T;
    }
}
