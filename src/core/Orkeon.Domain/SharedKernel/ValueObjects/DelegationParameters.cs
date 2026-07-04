using System.Collections.Immutable;
using System.Globalization;
using Orkeon.Generators;

namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// Strongly typed delegation parameters.
/// </summary>
[TypedDictionary(typeof(DelegationParameterValue), CacheEmpty = true)]
public sealed partial class DelegationParameters : IEquatable<DelegationParameters>
{
    /// <summary>
    /// Gets a parameter value.
    /// </summary>
    public T? Get<T>(string key)
    {
        if (!_items.TryGetValue(key, out var value))
            return default;

        return value.GetValue<T>();
    }

    /// <summary>
    /// Gets a required parameter value.
    /// </summary>
    public T GetRequired<T>(string key)
    {
        if (!_items.TryGetValue(key, out var value))
            throw new ArgumentException($"Required parameter '{key}' not found", nameof(key));

        return value.GetValue<T>();
    }

    /// <summary>
    /// Checks if a parameter exists.
    /// </summary>
    public bool Contains(string key) => _items.ContainsKey(key);

    /// <summary>
    /// Creates delegation parameters for skill-based delegation.
    /// </summary>
    public static DelegationParameters ForSkillBased(double minSkillMatch = 0.8)
    {
        return new Builder()
            .AddMinSkillMatch(minSkillMatch)
            .AddDelegationType(DelegationType.SkillBased)
            .Build();
    }

    /// <summary>
    /// Creates delegation parameters for workload-based delegation.
    /// </summary>
    public static DelegationParameters ForWorkloadBased(int maxWorkload = 5)
    {
        return new Builder()
            .AddMaxWorkload(maxWorkload)
            .AddDelegationType(DelegationType.WorkloadBased)
            .Build();
    }

    /// <summary>
    /// Creates delegation parameters for hierarchical delegation.
    /// </summary>
    public static DelegationParameters ForHierarchical(string managerRole)
    {
        return new Builder()
            .AddManagerRole(managerRole)
            .AddDelegationType(DelegationType.Hierarchical)
            .Build();
    }

    /// <inheritdoc />
    public bool Equals(DelegationParameters? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (_items.Count != other._items.Count) return false;

        foreach (var kvp in _items)
        {
            if (!other._items.TryGetValue(kvp.Key, out var otherValue))
                return false;
            if (!Equals(kvp.Value.RawValue, otherValue.RawValue))
                return false;
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as DelegationParameters);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_items.Count);
        foreach (var kvp in _items.OrderBy(k => k.Key))
        {
            hash.Add(kvp.Key);
            hash.Add(kvp.Value.RawValue);
        }
        return hash.ToHashCode();
    }

    /// <summary>Builder for constructing <see cref="DelegationParameters"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>Sets the minimum skill match threshold.</summary>
        /// <param name="value">The minimum skill match ratio (0.0–1.0).</param>
        /// <returns>This builder for chaining.</returns>
        public Builder AddMinSkillMatch(double value)
        {
            if (value < 0.0 || value > 1.0)
                throw new ArgumentOutOfRangeException(nameof(value),
                    $"MinSkillMatch must be between 0.0 and 1.0, but was {value}.");
            _items["minSkillMatch"] = DelegationParameterValue.From(value);
            return this;
        }

        /// <summary>Sets the maximum workload limit.</summary>
        /// <param name="value">The maximum number of concurrent tasks.</param>
        /// <returns>This builder for chaining.</returns>
        public Builder AddMaxWorkload(int value)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _items["maxWorkload"] = DelegationParameterValue.From(value);
            return this;
        }

        /// <summary>Sets the manager role for hierarchical delegation.</summary>
        /// <param name="value">The manager role name.</param>
        /// <returns>This builder for chaining.</returns>
        public Builder AddManagerRole(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _items["managerRole"] = DelegationParameterValue.From(value);
            return this;
        }

        /// <summary>Sets the priority weight for this delegation.</summary>
        /// <param name="value">The priority value.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("priority")]
        public partial Builder AddPriority(double value);

        /// <summary>Sets the delegation timeout.</summary>
        /// <param name="value">The timeout duration.</param>
        /// <returns>This builder for chaining.</returns>
        public Builder AddTimeout(TimeSpan value)
        {
            if (value <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(value), "Timeout must be positive.");
            _items["timeout"] = DelegationParameterValue.From(value);
            return this;
        }

        /// <summary>Sets the retry limit.</summary>
        /// <param name="value">The maximum number of retries.</param>
        /// <returns>This builder for chaining.</returns>
        public Builder AddRetryLimit(int value)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _items["retryLimit"] = DelegationParameterValue.From(value);
            return this;
        }

        /// <summary>Sets the delegation type.</summary>
        /// <param name="type">The delegation type.</param>
        /// <returns>This builder for chaining.</returns>
        public Builder AddDelegationType(DelegationType type)
        {
            ArgumentNullException.ThrowIfNull(type);
            _items["delegationType"] = DelegationParameterValue.From(type.ToString());
            return this;
        }
    }
}

/// <summary>
/// Represents a single delegation parameter value.
/// </summary>
public sealed class DelegationParameterValue
{
    private readonly object _value;
    private readonly Type _type;

    private DelegationParameterValue(object value, Type type)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
        ArgumentNullException.ThrowIfNull(type);
        _type = type;
    }

    /// <summary>Gets the typed value.</summary>
    /// <typeparam name="T">The expected type.</typeparam>
    /// <returns>The typed value.</returns>
    public T GetValue<T>()
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
                $"Cannot convert delegation parameter value of type {_type.Name} to {typeof(T).Name}", ex);
        }
    }

    /// <summary>Gets the raw underlying value.</summary>
    public object RawValue => _value;
    /// <summary>Gets the value's type.</summary>
    public Type ValueType => _type;

    /// <summary>Creates a <see cref="DelegationParameterValue"/> from the given object.</summary>
    /// <param name="value">The value to wrap.</param>
    /// <returns>A new <see cref="DelegationParameterValue"/>.</returns>
    public static DelegationParameterValue From(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new DelegationParameterValue(value, value.GetType());
    }
}
