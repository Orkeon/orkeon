namespace Orkeon.Domain.Common;

/// <summary>
/// Abstraction for the Dict-to-Model conversion pipeline used by ComponentBase.
/// Encapsulates parameter normalization, deserialization (Dict → TRequest),
/// and serialization (TResponse → Dict) so that the concrete JSON implementation
/// can live outside the Domain layer.
/// </summary>
public interface IComponentSerializer
{
    /// <summary>
    /// Normalizes parameter keys (e.g., snake_case conversion), unwraps
    /// implementation-specific value wrappers, then deserializes the dictionary
    /// into a strongly-typed request object.
    /// </summary>
    /// <typeparam name="T">The request type. Must be a class with a parameterless constructor.</typeparam>
    /// <param name="parameters">Raw parameter dictionary.</param>
    /// <returns>A deserialized instance of <typeparamref name="T"/>.</returns>
    T Deserialize<T>(Dictionary<string, object?> parameters) where T : class, new();

    /// <summary>
    /// Serializes a strongly-typed response object into a dictionary with
    /// normalized (snake_case) keys.
    /// </summary>
    /// <typeparam name="T">The response type.</typeparam>
    /// <param name="value">The response object to serialize.</param>
    /// <returns>A dictionary representation with snake_case keys.</returns>
    Dictionary<string, object?> Serialize<T>(T value) where T : class;

    /// <summary>
    /// Normalizes a parameter dictionary: converts keys to snake_case,
    /// unwraps implementation-specific value types (e.g., JsonElement).
    /// </summary>
    /// <param name="parameters">Raw parameter dictionary.</param>
    /// <returns>A normalized dictionary.</returns>
    Dictionary<string, object?> NormalizeParameters(Dictionary<string, object?> parameters);

    /// <summary>
    /// Recursively normalizes a single value: unwraps implementation-specific
    /// wrappers, normalizes nested dictionary keys, normalizes nested lists.
    /// </summary>
    /// <param name="value">The value to normalize.</param>
    /// <returns>The normalized value, or null.</returns>
    object? NormalizeValue(object? value);

    /// <summary>
    /// Converts a key from camelCase or PascalCase to snake_case.
    /// Preserves keys that are already snake_case, all-lowercase, or all-uppercase.
    /// </summary>
    /// <param name="key">The key to normalize.</param>
    /// <returns>The snake_case key.</returns>
    string NormalizeKeyToSnakeCase(string key);
}
