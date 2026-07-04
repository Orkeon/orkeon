using System.Collections.Immutable;
using System.Globalization;
using Orkeon.Domain.Constants.Http;
using Orkeon.Generators;

namespace Orkeon.Domain.Tools;

/// <summary>
/// Strongly typed tool call options.
/// </summary>
[TypedDictionary(typeof(ToolCallOptionValue), CacheEmpty = true)]
public sealed partial class ToolCallOptions
{
    /// <summary>
    /// Gets an option value.
    /// </summary>
    public T? Get<T>(string key)
    {
        if (!_items.TryGetValue(key, out var value))
            return default;

        return value.GetValue<T>();
    }

    /// <summary>
    /// Checks if an option exists.
    /// </summary>
    public bool Contains(string key) => _items.ContainsKey(key);

    /// <summary>
    /// Creates default tool call options.
    /// </summary>
    public static ToolCallOptions Default()
    {
        return new Builder()
            .AddTimeout(HttpDefaults.DefaultHttpTimeout)
            .AddRetryCount(3)
            .AddParallel(false)
            .Build();
    }

    /// <summary>
    /// Creates options for parallel tool execution.
    /// </summary>
    public static ToolCallOptions ForParallelExecution(int maxConcurrency = 5)
    {
        return new Builder()
            .AddParallel(true)
            .AddMaxConcurrency(maxConcurrency)
            .AddTimeout(HttpDefaults.DefaultToolParallelTimeout)
            .Build();
    }

    /// <summary>Builder for constructing <see cref="ToolCallOptions"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>Sets the timeout for tool calls.</summary>
        /// <param name="timeout">The timeout duration.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("timeout")]
        public partial Builder AddTimeout(TimeSpan timeout);

        /// <summary>Sets the retry count for failed tool calls.</summary>
        /// <param name="retries">The number of retries.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("retryCount")]
        public partial Builder AddRetryCount(int retries);

        /// <summary>Sets whether to execute tool calls in parallel.</summary>
        /// <param name="parallel">Whether parallel execution is enabled.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("parallel")]
        public partial Builder AddParallel(bool parallel);

        /// <summary>Sets the maximum concurrency for parallel tool calls.</summary>
        /// <param name="maxConcurrency">The maximum number of concurrent calls.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("maxConcurrency")]
        public partial Builder AddMaxConcurrency(int maxConcurrency);

        /// <summary>Sets whether to validate arguments before calling tools.</summary>
        /// <param name="validate">Whether validation is enabled.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("validateArgs")]
        public partial Builder AddValidation(bool validate);

        /// <summary>Sets whether to enable tracing for tool calls.</summary>
        /// <param name="enableTracing">Whether tracing is enabled.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("tracing")]
        public partial Builder AddTracing(bool enableTracing);
    }
}

/// <summary>
/// Represents a single tool call option value.
/// </summary>
public sealed class ToolCallOptionValue
{
    private readonly object _value;
    private readonly Type _type;

    private ToolCallOptionValue(object value, Type type)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
        ArgumentNullException.ThrowIfNull(type);
        _type = type;
    }

    /// <summary>Gets the stored value as the specified type.</summary>
    /// <typeparam name="T">The expected type.</typeparam>
    /// <returns>The value as type <typeparamref name="T"/>.</returns>
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
                $"Cannot convert tool call option value of type {_type.Name} to {typeof(T).Name}", ex);
        }
    }

    /// <summary>Gets the raw object value.</summary>
    public object RawValue => _value;
    /// <summary>Gets the runtime type of the stored value.</summary>
    public Type ValueType => _type;

    /// <summary>Creates a new <see cref="ToolCallOptionValue"/> wrapping the given object.</summary>
    /// <param name="value">The value to wrap.</param>
    /// <returns>A new <see cref="ToolCallOptionValue"/>.</returns>
    public static ToolCallOptionValue From(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new ToolCallOptionValue(value, value.GetType());
    }
}
