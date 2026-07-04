
namespace Orkeon.Application.Interfaces.Infrastructure.Serialization;

/// <summary>
/// Interface for CSV serialization operations.
/// </summary>
public interface ICsvSerializer
{
    /// <summary>
    /// Serializes a collection of objects to CSV string.
    /// </summary>
    /// <typeparam name="T">The type of objects to serialize.</typeparam>
    /// <param name="records">The collection of objects to serialize.</param>
    /// <returns>The CSV string representation.</returns>
    string Serialize<T>(IEnumerable<T> records);

    /// <summary>
    /// Serializes a collection of objects to CSV and writes to a stream.
    /// </summary>
    /// <typeparam name="T">The type of objects to serialize.</typeparam>
    /// <param name="records">The collection of objects to serialize.</param>
    /// <param name="stream">The stream to write to.</param>
    System.Threading.Tasks.Task SerializeAsync<T>(IEnumerable<T> records, Stream stream);

    /// <summary>
    /// Deserializes CSV string to a collection of objects.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="csv">The CSV string.</param>
    /// <returns>The collection of deserialized objects.</returns>
    IEnumerable<T> Deserialize<T>(string csv);

    /// <summary>
    /// Deserializes CSV from a stream to a collection of objects.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="stream">The stream to read from.</param>
    /// <returns>The collection of deserialized objects.</returns>
    System.Threading.Tasks.Task<IEnumerable<T>> DeserializeAsync<T>(Stream stream);

    /// <summary>
    /// Deserializes CSV string to dynamic objects.
    /// </summary>
    /// <param name="csv">The CSV string.</param>
    /// <returns>The collection of dynamic objects.</returns>
    IEnumerable<dynamic> DeserializeDynamic(string csv);
}
