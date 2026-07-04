namespace Orkeon.Application.Interfaces.Infrastructure.Serialization;

/// <summary>
/// Interface for YAML serialization operations.
/// </summary>
public interface IYamlSerializer
{
    /// <summary>
    /// Serializes an object to YAML string.
    /// </summary>
    /// <typeparam name="T">The type of object to serialize.</typeparam>
    /// <param name="obj">The object to serialize.</param>
    /// <returns>The YAML string representation.</returns>
    string Serialize<T>(T obj);

    /// <summary>
    /// Deserializes a YAML string to an object.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="yaml">The YAML string.</param>
    /// <returns>The deserialized object.</returns>
    T Deserialize<T>(string yaml);

    /// <summary>
    /// Deserializes a YAML string to a dynamic object.
    /// </summary>
    /// <param name="yaml">The YAML string.</param>
    /// <returns>The deserialized dynamic object.</returns>
    object Deserialize(string yaml);
}
