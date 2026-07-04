namespace Orkeon.Domain.Crew.ValueObjects;

/// <summary>
/// Strongly typed, immutable crew variables container.
/// Set methods return new instances; the original is never modified.
/// </summary>
public sealed class CrewVariables : IEquatable<CrewVariables>
{
    private readonly Dictionary<string, string> _strings;
    private readonly Dictionary<string, int> _integers;
    private readonly Dictionary<string, bool> _booleans;
    private readonly Dictionary<string, double> _numbers;

    /// <summary>Gets an empty <see cref="CrewVariables"/> instance.</summary>
    public static CrewVariables Empty => new CrewVariables();

    /// <summary>Creates an empty <see cref="CrewVariables"/> instance.</summary>
    private CrewVariables()
    {
        _strings = [];
        _integers = [];
        _booleans = [];
        _numbers = [];
    }

    private CrewVariables(
        Dictionary<string, string> strings,
        Dictionary<string, int> integers,
        Dictionary<string, bool> booleans,
        Dictionary<string, double> numbers)
    {
        _strings = new Dictionary<string, string>(strings);
        _integers = new Dictionary<string, int>(integers);
        _booleans = new Dictionary<string, bool>(booleans);
        _numbers = new Dictionary<string, double>(numbers);
    }

    /// <summary>Returns a new <see cref="CrewVariables"/> with the given string variable set.</summary>
    /// <param name="key">The variable key.</param>
    /// <param name="value">The string value.</param>
    /// <returns>A new instance with the variable set.</returns>
    public CrewVariables Set(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var copy = new Dictionary<string, string>(_strings) { [key] = value };
        return new CrewVariables(copy, new Dictionary<string, int>(_integers), new Dictionary<string, bool>(_booleans), new Dictionary<string, double>(_numbers));
    }

    /// <summary>Returns a new <see cref="CrewVariables"/> with the given integer variable set.</summary>
    /// <param name="key">The variable key.</param>
    /// <param name="value">The integer value.</param>
    /// <returns>A new instance with the variable set.</returns>
    public CrewVariables Set(string key, int value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var copy = new Dictionary<string, int>(_integers) { [key] = value };
        return new CrewVariables(new Dictionary<string, string>(_strings), copy, new Dictionary<string, bool>(_booleans), new Dictionary<string, double>(_numbers));
    }

    /// <summary>Returns a new <see cref="CrewVariables"/> with the given boolean variable set.</summary>
    /// <param name="key">The variable key.</param>
    /// <param name="value">The boolean value.</param>
    /// <returns>A new instance with the variable set.</returns>
    public CrewVariables Set(string key, bool value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var copy = new Dictionary<string, bool>(_booleans) { [key] = value };
        return new CrewVariables(new Dictionary<string, string>(_strings), new Dictionary<string, int>(_integers), copy, new Dictionary<string, double>(_numbers));
    }

    /// <summary>Returns a new <see cref="CrewVariables"/> with the given double variable set.</summary>
    /// <param name="key">The variable key.</param>
    /// <param name="value">The double value.</param>
    /// <returns>A new instance with the variable set.</returns>
    public CrewVariables Set(string key, double value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var copy = new Dictionary<string, double>(_numbers) { [key] = value };
        return new CrewVariables(new Dictionary<string, string>(_strings), new Dictionary<string, int>(_integers), new Dictionary<string, bool>(_booleans), copy);
    }

    /// <summary>Gets a typed struct variable by key.</summary>
    /// <typeparam name="T">The expected value type (int, bool, or double).</typeparam>
    /// <param name="key">The variable key.</param>
    /// <returns>The typed value, or <see langword="null"/> if not found.</returns>
    public T? Get<T>(string key) where T : struct
    {
        if (typeof(T) == typeof(int) && _integers.TryGetValue(key, out var intValue))
            return (T)(object)intValue;

        if (typeof(T) == typeof(bool) && _booleans.TryGetValue(key, out var boolValue))
            return (T)(object)boolValue;

        if (typeof(T) == typeof(double) && _numbers.TryGetValue(key, out var doubleValue))
            return (T)(object)doubleValue;

        return null;
    }

    /// <summary>Gets a string variable by key.</summary>
    /// <param name="key">The variable key.</param>
    /// <returns>The string value, or <see langword="null"/> if not found.</returns>
    public string? GetString(string key) => _strings.GetValueOrDefault(key);

    /// <summary>Converts all variables to a flat <see cref="Dictionary{TKey,TValue}"/> of objects.</summary>
    /// <returns>A dictionary containing all variables as boxed values.</returns>
    public Dictionary<string, object> ToDictionary()
    {
        var result = new Dictionary<string, object>();

        foreach (var kvp in _strings)
            result[kvp.Key] = kvp.Value;

        foreach (var kvp in _integers)
            result[kvp.Key] = kvp.Value;

        foreach (var kvp in _booleans)
            result[kvp.Key] = kvp.Value;

        foreach (var kvp in _numbers)
            result[kvp.Key] = kvp.Value;

        return result;
    }

    /// <summary>Creates a <see cref="CrewVariables"/> from a dictionary of boxed values.</summary>
    /// <param name="dict">The source dictionary.</param>
    /// <returns>A new <see cref="CrewVariables"/> populated from the dictionary.</returns>
    public static CrewVariables FromDictionary(Dictionary<string, object> dict)
    {
        ArgumentNullException.ThrowIfNull(dict);
        var strings = new Dictionary<string, string>();
        var integers = new Dictionary<string, int>();
        var booleans = new Dictionary<string, bool>();
        var numbers = new Dictionary<string, double>();

        foreach (var kvp in dict)
        {
            switch (kvp.Value)
            {
                case string str:
                    strings[kvp.Key] = str;
                    break;
                case int intVal:
                    integers[kvp.Key] = intVal;
                    break;
                case bool boolVal:
                    booleans[kvp.Key] = boolVal;
                    break;
                case double doubleVal:
                    numbers[kvp.Key] = doubleVal;
                    break;
                case float floatVal:
                    numbers[kvp.Key] = (double)floatVal;
                    break;
                default:
                    // Convert to string as fallback
                    strings[kvp.Key] = kvp.Value?.ToString() ?? string.Empty;
                    break;
            }
        }

        return new CrewVariables(strings, integers, booleans, numbers);
    }

    /// <inheritdoc />
    public bool Equals(CrewVariables? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return DictionariesEqual(_strings, other._strings)
            && DictionariesEqual(_integers, other._integers)
            && DictionariesEqual(_booleans, other._booleans)
            && DictionariesEqual(_numbers, other._numbers);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as CrewVariables);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var kvp in _strings.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            hash.Add(kvp.Key, StringComparer.Ordinal);
            hash.Add(kvp.Value, StringComparer.Ordinal);
        }

        foreach (var kvp in _integers.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            hash.Add(kvp.Key, StringComparer.Ordinal);
            hash.Add(kvp.Value);
        }

        foreach (var kvp in _booleans.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            hash.Add(kvp.Key, StringComparer.Ordinal);
            hash.Add(kvp.Value);
        }

        foreach (var kvp in _numbers.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            hash.Add(kvp.Key, StringComparer.Ordinal);
            hash.Add(kvp.Value);
        }

        return hash.ToHashCode();
    }

    /// <summary>Equality operator.</summary>
    public static bool operator ==(CrewVariables? left, CrewVariables? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(CrewVariables? left, CrewVariables? right)
        => !(left == right);

    private static bool DictionariesEqual<TValue>(
        Dictionary<string, TValue> left,
        Dictionary<string, TValue> right)
    {
        if (left.Count != right.Count) return false;

        foreach (var kvp in left)
        {
            if (!right.TryGetValue(kvp.Key, out var value)
                || !EqualityComparer<TValue>.Default.Equals(kvp.Value, value))
                return false;
        }

        return true;
    }
}
