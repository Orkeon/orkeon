using System.Collections.Immutable;
using System.Globalization;
using Orkeon.Generators;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>
/// Strongly typed LLM response metadata.
/// </summary>
[TypedDictionary(typeof(LlmMetadataValue))]
public sealed partial class LlmResponseMetadata
{
    /// <summary>Gets a typed metadata value by key.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="key">The metadata key.</param>
    /// <returns>The typed value, or default if not found.</returns>
    public T? Get<T>(string key)
    {
        if (!_items.TryGetValue(key, out var value))
            return default;

        return value.GetValue<T>();
    }

    /// <summary>Converts the metadata to a plain dictionary.</summary>
    /// <returns>A dictionary of key-value pairs.</returns>
    public Dictionary<string, object> ToDictionary()
    {
        var result = new Dictionary<string, object>();
        foreach (var kvp in _items)
        {
            result[kvp.Key] = kvp.Value.RawValue;
        }
        return result;
    }

    /// <summary>Builder for constructing <see cref="LlmResponseMetadata"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>Adds the provider name.</summary>
        /// <param name="provider">The provider name.</param>
        /// <returns>This builder.</returns>
        [DictionaryEntry("provider")]
        public partial Builder AddProvider(string provider);

        /// <summary>Adds the error message.</summary>
        /// <param name="error">The error message.</param>
        /// <returns>This builder.</returns>
        [DictionaryEntry("error")]
        public partial Builder AddError(string error);

        /// <summary>Adds the error type.</summary>
        /// <param name="errorType">The error type name.</param>
        /// <returns>This builder.</returns>
        [DictionaryEntry("error_type")]
        public partial Builder AddErrorType(string errorType);

        /// <summary>Adds the done flag.</summary>
        /// <param name="done">Whether the response is complete.</param>
        /// <returns>This builder.</returns>
        [DictionaryEntry("done")]
        public partial Builder AddDone(bool done);

        /// <summary>Adds the total duration nanoseconds.</summary>
        /// <param name="duration">The total duration in nanoseconds.</param>
        /// <returns>This builder.</returns>
        [DictionaryEntry("total_duration")]
        public partial Builder AddTotalDuration(long duration);

        /// <summary>Adds the eval duration nanoseconds.</summary>
        /// <param name="duration">The eval duration in nanoseconds.</param>
        /// <returns>This builder.</returns>
        [DictionaryEntry("eval_duration")]
        public partial Builder AddEvalDuration(long duration);
    }
}

/// <summary>
/// Ollama request payload options.
/// </summary>
[TypedDictionary(typeof(LlmMetadataValue))]
public sealed partial class OllamaRequestOptions
{
    /// <summary>Converts the options to a plain dictionary.</summary>
    /// <returns>A dictionary of key-value pairs.</returns>
    public Dictionary<string, object> ToDictionary()
    {
        var result = new Dictionary<string, object>();
        foreach (var kvp in _items)
        {
            result[kvp.Key] = kvp.Value.RawValue;
        }
        return result;
    }

    /// <summary>Builder for constructing <see cref="OllamaRequestOptions"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>Sets the sampling temperature.</summary>
        /// <param name="temperature">The temperature value.</param>
        /// <returns>This builder.</returns>
        [DictionaryEntry("temperature")]
        public partial Builder AddTemperature(double temperature);

        /// <summary>Sets the maximum number of tokens to predict.</summary>
        /// <param name="maxTokens">The maximum token count.</param>
        /// <returns>This builder.</returns>
        public Builder AddNumPredict(int maxTokens)
        {
            if (maxTokens > 0)
            {
                _items["num_predict"] = LlmMetadataValue.From(maxTokens);
            }
            return this;
        }

        /// <summary>Sets the top-k sampling parameter.</summary>
        /// <param name="topK">The top-k value.</param>
        /// <returns>This builder.</returns>
        [DictionaryEntry("top_k")]
        public partial Builder AddTopK(int topK);

        /// <summary>Sets the top-p nucleus sampling parameter.</summary>
        /// <param name="topP">The top-p value.</param>
        /// <returns>This builder.</returns>
        [DictionaryEntry("top_p")]
        public partial Builder AddTopP(double topP);

        /// <summary>Sets the random seed for reproducible generation.</summary>
        /// <param name="seed">The seed value.</param>
        /// <returns>This builder.</returns>
        [DictionaryEntry("seed")]
        public partial Builder AddSeed(int seed);
    }
}

/// <summary>
/// Ollama request payload.
/// </summary>
[TypedDictionary(typeof(LlmMetadataValue), EmitEmpty = false, EmitGenericAdd = false)]
public sealed partial class OllamaRequestPayload
{
    /// <summary>Converts the payload to a plain dictionary.</summary>
    /// <returns>A dictionary of key-value pairs.</returns>
    public Dictionary<string, object> ToDictionary()
    {
        var result = new Dictionary<string, object>();
        foreach (var kvp in _items)
        {
            result[kvp.Key] = kvp.Value.RawValue;
        }
        return result;
    }

    /// <summary>Builder for constructing <see cref="OllamaRequestPayload"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>Sets the model identifier.</summary>
        /// <param name="model">The model name.</param>
        /// <returns>This builder.</returns>
        [DictionaryEntry("model")]
        public partial Builder AddModel(string model);

        /// <summary>Sets the prompt text.</summary>
        /// <param name="prompt">The prompt text.</param>
        /// <returns>This builder.</returns>
        [DictionaryEntry("prompt")]
        public partial Builder AddPrompt(string prompt);

        /// <summary>Sets whether to stream the response.</summary>
        /// <param name="stream">Whether streaming is enabled.</param>
        /// <returns>This builder.</returns>
        [DictionaryEntry("stream")]
        public partial Builder AddStream(bool stream);

        /// <summary>Sets the request options.</summary>
        /// <param name="options">The Ollama request options.</param>
        /// <returns>This builder.</returns>
        public Builder AddOptions(OllamaRequestOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            _items["options"] = LlmMetadataValue.From(options.ToDictionary());
            return this;
        }

        /// <summary>Sets a GBNF grammar used by the llama.cpp backend to constrain generation.</summary>
        /// <param name="grammarGbnf">The GBNF grammar string (non-empty).</param>
        /// <returns>This builder.</returns>
        [DictionaryEntry("grammar")]
        public partial Builder AddGrammar(string grammarGbnf);

        /// <summary>
        /// Sets Ollama's output-format constraint. Accepts the literal <c>"json"</c> or a
        /// complete JSON Schema document — hence the untyped parameter, which the typed
        /// <c>[DictionaryEntry]</c> generator cannot express.
        /// </summary>
        /// <param name="format">The format value: <c>"json"</c> or a schema object.</param>
        /// <returns>This builder.</returns>
        public Builder AddFormat(object format)
        {
            ArgumentNullException.ThrowIfNull(format);
            _items["format"] = LlmMetadataValue.From(format);
            return this;
        }

        /// <summary>
        /// Sets Ollama's thinking switch. Accepts a boolean or one of
        /// <c>"low"</c>/<c>"medium"</c>/<c>"high"</c>, so the parameter is untyped.
        /// </summary>
        /// <param name="think">The thinking value: a boolean or an effort level.</param>
        /// <returns>This builder.</returns>
        public Builder AddThink(object think)
        {
            ArgumentNullException.ThrowIfNull(think);
            _items["think"] = LlmMetadataValue.From(think);
            return this;
        }
    }
}

/// <summary>
/// LLM metadata value wrapper.
/// </summary>
public sealed class LlmMetadataValue
{
    private readonly object _value;
    private readonly Type _type;

    private LlmMetadataValue(object value, Type type)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
        ArgumentNullException.ThrowIfNull(type);
        _type = type;
    }

    /// <summary>Gets the value as the specified type.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <returns>The typed value.</returns>
    public T GetValue<T>()
    {
        if (_value is T typedValue)
            return typedValue;

        try
        {
            return (T)Convert.ChangeType(_value, typeof(T), CultureInfo.InvariantCulture);
        }
        catch (InvalidCastException ex)
        {
            throw new InvalidCastException(
                $"Cannot convert LLM metadata value of type {_type.Name} to {typeof(T).Name}", ex);
        }
        catch (FormatException ex)
        {
            throw new InvalidCastException(
                $"Cannot convert LLM metadata value of type {_type.Name} to {typeof(T).Name}", ex);
        }
        catch (OverflowException ex)
        {
            throw new InvalidCastException(
                $"Cannot convert LLM metadata value of type {_type.Name} to {typeof(T).Name}", ex);
        }
    }

    /// <summary>Gets the raw underlying value.</summary>
    public object RawValue => _value;
    /// <summary>Gets the CLR type of the stored value.</summary>
    public Type ValueType => _type;

    /// <summary>Creates a new <see cref="LlmMetadataValue"/> wrapping the given object.</summary>
    /// <param name="value">The value to wrap.</param>
    /// <returns>A new metadata value instance.</returns>
    public static LlmMetadataValue From(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new LlmMetadataValue(value, value.GetType());
    }
}
