using System.Collections.Immutable;

namespace Orkeon.Domain.Task.ValueObjects;

/// <summary>
/// Strongly typed task execution variables.
/// </summary>
public sealed class TaskVariables : IEquatable<TaskVariables>
{
    private readonly ImmutableDictionary<string, string> _strings;
    private readonly ImmutableDictionary<string, int> _integers;
    private readonly ImmutableDictionary<string, bool> _booleans;
    private readonly ImmutableDictionary<string, double> _numbers;
    private readonly ImmutableDictionary<string, object> _complexValues;

    private TaskVariables(
        ImmutableDictionary<string, string> strings,
        ImmutableDictionary<string, int> integers,
        ImmutableDictionary<string, bool> booleans,
        ImmutableDictionary<string, double> numbers,
        ImmutableDictionary<string, object> complexValues)
    {
        _strings = strings;
        _integers = integers;
        _booleans = booleans;
        _numbers = numbers;
        _complexValues = complexValues;
    }

    private static readonly TaskVariables _empty = new(
        [],
        [],
        [],
        [],
        []);
    /// <summary>Gets an empty <see cref="TaskVariables"/> instance.</summary>
    public static TaskVariables Empty => _empty;

    /// <summary>
    /// Gets a string variable.
    /// </summary>
    public string? GetString(string key) =>
        _strings.TryGetValue(key, out var value) ? value : null;

    /// <summary>
    /// Gets an integer variable.
    /// </summary>
    public int? GetInt(string key) =>
        _integers.TryGetValue(key, out var value) ? value : null;

    /// <summary>
    /// Gets a boolean variable.
    /// </summary>
    public bool? GetBool(string key) =>
        _booleans.TryGetValue(key, out var value) ? value : null;

    /// <summary>
    /// Gets a double variable.
    /// </summary>
    public double? GetDouble(string key) =>
        _numbers.TryGetValue(key, out var value) ? value : null;

    /// <summary>
    /// Gets a complex object variable.
    /// </summary>
    public T? GetObject<T>(string key) where T : class =>
        _complexValues.TryGetValue(key, out var value) && value is T typedValue
            ? typedValue
            : null;

    /// <summary>
    /// Gets all variable keys.
    /// </summary>
    public IEnumerable<string> Keys =>
        _strings.Keys
            .Concat(_integers.Keys)
            .Concat(_booleans.Keys)
            .Concat(_numbers.Keys)
            .Concat(_complexValues.Keys)
            .Distinct();

    /// <summary>
    /// Creates a new TaskVariables with an added/updated variable.
    /// </summary>
    public TaskVariables With<T>(string key, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return value switch
        {
            string str => new TaskVariables(
                _strings.SetItem(key, str),
                _integers,
                _booleans,
                _numbers,
                _complexValues),

            int i => new TaskVariables(
                _strings,
                _integers.SetItem(key, i),
                _booleans,
                _numbers,
                _complexValues),

            bool b => new TaskVariables(
                _strings,
                _integers,
                _booleans.SetItem(key, b),
                _numbers,
                _complexValues),

            double d => new TaskVariables(
                _strings,
                _integers,
                _booleans,
                _numbers.SetItem(key, d),
                _complexValues),

            _ => new TaskVariables(
                _strings,
                _integers,
                _booleans,
                _numbers,
                _complexValues.SetItem(key, value!))
        };
    }

    /// <inheritdoc />
    public bool Equals(TaskVariables? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return DictsEqual(_strings, other._strings)
            && DictsEqual(_integers, other._integers)
            && DictsEqual(_booleans, other._booleans)
            && DictsEqual(_numbers, other._numbers)
            && DictsEqual(_complexValues, other._complexValues);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as TaskVariables);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        AddDictHash(hash, _strings);
        AddDictHash(hash, _integers);
        AddDictHash(hash, _booleans);
        AddDictHash(hash, _numbers);
        hash.Add(_complexValues.Count);
        return hash.ToHashCode();
    }

    private static bool DictsEqual<T>(ImmutableDictionary<string, T> a, ImmutableDictionary<string, T> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var kvp in a)
        {
            if (!b.TryGetValue(kvp.Key, out var otherValue))
                return false;
            if (!EqualityComparer<T>.Default.Equals(kvp.Value, otherValue))
                return false;
        }
        return true;
    }

    private static void AddDictHash<T>(HashCode hash, ImmutableDictionary<string, T> dict)
    {
        hash.Add(dict.Count);
        foreach (var kvp in dict.OrderBy(k => k.Key))
        {
            hash.Add(kvp.Key);
            hash.Add(kvp.Value);
        }
    }

    /// <summary>
    /// Creates a builder for constructing TaskVariables.
    /// </summary>
    public static Builder CreateBuilder() => new();


    /// <summary>
    /// Builder for constructing TaskVariables.
    /// </summary>
    public sealed class Builder
    {
        private readonly Dictionary<string, string> _strings = [];
        private readonly Dictionary<string, int> _integers = [];
        private readonly Dictionary<string, bool> _booleans = [];
        private readonly Dictionary<string, double> _numbers = [];
        private readonly Dictionary<string, object> _complexValues = [];

        /// <summary>
        /// Adds a string variable.
        /// </summary>
        public Builder AddString(string key, string value)
        {
            _strings[key] = value;
            return this;
        }

        /// <summary>
        /// Adds an integer variable.
        /// </summary>
        public Builder AddInt(string key, int value)
        {
            _integers[key] = value;
            return this;
        }

        /// <summary>
        /// Adds a boolean variable.
        /// </summary>
        public Builder AddBool(string key, bool value)
        {
            _booleans[key] = value;
            return this;
        }

        /// <summary>
        /// Adds a double variable.
        /// </summary>
        public Builder AddDouble(string key, double value)
        {
            _numbers[key] = value;
            return this;
        }

        /// <summary>
        /// Adds an object variable.
        /// </summary>
        public Builder AddObject<T>(string key, T value) where T : class
        {
            // Handle primitive types specially
            if (value is string str)
            {
                _strings[key] = str;
            }
            else
            {
                _complexValues[key] = value;
            }
            return this;
        }


        /// <summary>
        /// Builds the immutable TaskVariables.
        /// </summary>
        public TaskVariables Build()
        {
            return new TaskVariables(
                _strings.ToImmutableDictionary(),
                _integers.ToImmutableDictionary(),
                _booleans.ToImmutableDictionary(),
                _numbers.ToImmutableDictionary(),
                _complexValues.ToImmutableDictionary());
        }
    }
}
