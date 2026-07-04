using System.Collections.Immutable;
using System.Globalization;
using Orkeon.Generators;

namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// Base class for typed parameter collections.
/// </summary>
#pragma warning disable S4035 // Abstract base; sealed concrete subclasses handle equality correctly
public abstract class TypedParameters : IEquatable<TypedParameters>
#pragma warning restore S4035
{
    private readonly ImmutableDictionary<string, ParameterValue> _parameters;

    /// <summary>Initializes the parameter collection from an immutable dictionary.</summary>
    /// <param name="parameters">The initial parameters.</param>
    protected TypedParameters(ImmutableDictionary<string, ParameterValue> parameters)
    {
        _parameters = parameters ?? [];
    }

    /// <summary>Gets a required typed parameter value by key.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="key">The parameter key.</param>
    /// <returns>The typed value.</returns>
    /// <exception cref="ArgumentException">Thrown if the key does not exist.</exception>
    public T GetRequired<T>(string key)
    {
        if (!_parameters.TryGetValue(key, out var value))
            throw new ArgumentException($"Required parameter '{key}' not found", nameof(key));

        return value.GetValue<T>();
    }

    /// <summary>Gets an optional typed parameter value by key.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="key">The parameter key.</param>
    /// <returns>The typed value, or <see langword="default"/> if not found.</returns>
    public T? GetOptional<T>(string key)
    {
        if (!_parameters.TryGetValue(key, out var value))
            return default;

        return value.GetValue<T>();
    }

    /// <summary>Gets an optional typed parameter value by key, returning a default value if not found.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="key">The parameter key.</param>
    /// <param name="defaultValue">The value to return when the key is not found.</param>
    /// <returns>The typed value, or <paramref name="defaultValue"/> if not found.</returns>
    public T GetOptional<T>(string key, T defaultValue)
    {
        if (!_parameters.TryGetValue(key, out var value))
            return defaultValue;

        return value.GetValue<T>();
    }

    /// <summary>Returns whether a parameter exists for the given key.</summary>
    /// <param name="key">The parameter key.</param>
    /// <returns><see langword="true"/> if the key exists; otherwise <see langword="false"/>.</returns>
    public bool Contains(string key) => _parameters.ContainsKey(key);

    /// <summary>Gets all parameter keys.</summary>
    public IEnumerable<string> Keys => _parameters.Keys;

    /// <summary>Gets the number of parameters.</summary>
    public int Count => _parameters.Count;

    /// <inheritdoc />
    public bool Equals(TypedParameters? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (GetType() != other.GetType()) return false;
        if (_parameters.Count != other._parameters.Count) return false;

        foreach (var kvp in _parameters)
        {
            if (!other._parameters.TryGetValue(kvp.Key, out var otherValue))
                return false;
            if (!Equals(kvp.Value.RawValue, otherValue.RawValue))
                return false;
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as TypedParameters);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(GetType());
        hash.Add(_parameters.Count);
        foreach (var kvp in _parameters.OrderBy(k => k.Key))
        {
            hash.Add(kvp.Key);
            hash.Add(kvp.Value.RawValue);
        }
        return hash.ToHashCode();
    }

    /// <summary>Gets the underlying immutable parameter dictionary.</summary>
    protected ImmutableDictionary<string, ParameterValue> Parameters => _parameters;
}

/// <summary>
/// Communication protocol parameters.
/// </summary>
[TypedDictionary(typeof(ParameterValue), CacheEmpty = true)]
public sealed partial class CommunicationParameters : TypedParameters
{
    /// <summary>Returns a new instance with the specified parameter set.</summary>
    /// <param name="key">The parameter key.</param>
    /// <param name="value">The value.</param>
    /// <returns>A new <see cref="CommunicationParameters"/> with the value set.</returns>
    public CommunicationParameters With(string key, object value)
    {
        return new CommunicationParameters(Parameters.SetItem(key, ParameterValue.From(value)));
    }

    /// <summary>Builder for constructing <see cref="CommunicationParameters"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>Sets the communication timeout.</summary>
        /// <param name="timeout">The timeout duration.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("timeout")]
        public partial Builder AddTimeout(TimeSpan timeout);

        /// <summary>Sets the maximum retry count.</summary>
        /// <param name="retries">The number of retries allowed.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("retryCount")]
        public partial Builder AddRetryCount(int retries);

        /// <summary>Sets whether encryption is enabled.</summary>
        /// <param name="encrypted">Whether to encrypt communication.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("encrypted")]
        public partial Builder AddEncryption(bool encrypted);

        /// <summary>Sets the compression type.</summary>
        /// <param name="compressionType">The compression algorithm name.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("compression")]
        public partial Builder AddCompression(string compressionType);
    }
}

/// <summary>
/// Execution context parameters.
/// </summary>
[TypedDictionary(typeof(ParameterValue), CacheEmpty = true)]
public sealed partial class ExecutionContextParameters : TypedParameters
{
    /// <summary>Returns a new instance with the specified parameter set.</summary>
    /// <param name="key">The parameter key.</param>
    /// <param name="value">The value.</param>
    /// <returns>A new <see cref="ExecutionContextParameters"/> with the value set.</returns>
    public ExecutionContextParameters With(string key, object value)
    {
        return new ExecutionContextParameters(Parameters.SetItem(key, ParameterValue.From(value)));
    }

    /// <summary>Builder for constructing <see cref="ExecutionContextParameters"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>Sets the working directory path.</summary>
        /// <param name="path">The working directory path.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("workingDirectory")]
        public partial Builder AddWorkingDirectory(string path);

        /// <summary>Sets an environment variable.</summary>
        /// <param name="name">The environment variable name.</param>
        /// <param name="value">The environment variable value.</param>
        /// <returns>This builder for chaining.</returns>
        public Builder AddEnvironmentVariable(string name, string value)
        {
            var envKey = $"env_{name}";
            _items[envKey] = ParameterValue.From(value);
            return this;
        }

        /// <summary>Sets the maximum memory allowed in bytes.</summary>
        /// <param name="bytes">The memory limit in bytes.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("maxMemory")]
        public partial Builder AddMaxMemory(long bytes);

        /// <summary>Sets the maximum CPU usage percentage.</summary>
        /// <param name="percentage">The CPU limit as a percentage (0–100).</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("maxCpu")]
        public partial Builder AddMaxCpu(double percentage);
    }
}

/// <summary>
/// Execution step parameters.
/// </summary>
[TypedDictionary(typeof(ParameterValue), CacheEmpty = true)]
public sealed partial class ExecutionStepParameters : TypedParameters
{
    /// <summary>Returns a new instance with the specified parameter set.</summary>
    /// <param name="key">The parameter key.</param>
    /// <param name="value">The value.</param>
    /// <returns>A new <see cref="ExecutionStepParameters"/> with the value set.</returns>
    public ExecutionStepParameters With(string key, object value)
    {
        return new ExecutionStepParameters(Parameters.SetItem(key, ParameterValue.From(value)));
    }

    /// <summary>Builder for constructing <see cref="ExecutionStepParameters"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>Sets a named input value for the step.</summary>
        /// <param name="name">The input name.</param>
        /// <param name="value">The input value.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("input_{0}")]
        public partial Builder AddInput(string name, object value);

        /// <summary>Sets a named output value for the step.</summary>
        /// <param name="name">The output name.</param>
        /// <param name="value">The output value.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("output_{0}")]
        public partial Builder AddOutput(string name, object value);

        /// <summary>Sets the step condition expression.</summary>
        /// <param name="condition">The condition expression.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("condition")]
        public partial Builder AddCondition(string condition);

        /// <summary>Sets the step priority.</summary>
        /// <param name="priority">The priority value.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("priority")]
        public partial Builder AddPriority(int priority);
    }
}

/// <summary>
/// Represents a single parameter value with type information.
/// </summary>
public sealed class ParameterValue
{
    private readonly object _value;
    private readonly Type _type;

    private ParameterValue(object value, Type type)
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
            // Special handling for double to int conversion to truncate instead of round
            if (typeof(T) == typeof(int) && _value is double doubleValue)
            {
                return (T)(object)(int)doubleValue;
            }

            return (T)Convert.ChangeType(_value, typeof(T), CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            throw new InvalidCastException(
                $"Cannot convert parameter value of type {_type.Name} to {typeof(T).Name}", ex);
        }
    }

    /// <summary>Gets the raw underlying value.</summary>
    public object RawValue => _value;
    /// <summary>Gets the value's type.</summary>
    public Type ValueType => _type;

    /// <summary>Creates a <see cref="ParameterValue"/> from the given object.</summary>
    /// <param name="value">The value to wrap.</param>
    /// <returns>A new <see cref="ParameterValue"/>.</returns>
    public static ParameterValue From(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new ParameterValue(value, value.GetType());
    }
}
