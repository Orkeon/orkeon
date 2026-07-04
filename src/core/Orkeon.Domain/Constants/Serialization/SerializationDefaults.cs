namespace Orkeon.Domain.Constants.Serialization;

/// <summary>
/// Centralised serialization constants used across the Orkeon platform.
/// Eliminates scattered magic values and provides a single place to tune
/// serialization defaults.
/// </summary>
public static class SerializationDefaults
{
    /// <summary>
    /// Default maximum depth for JSON serialization/deserialization (32).
    /// Used in <see cref="System.Text.Json.JsonSerializerOptions.MaxDepth"/>
    /// and <see cref="System.Text.Json.JsonDocumentOptions.MaxDepth"/>.
    /// </summary>
    public const int JsonMaxDepth = 32;
}
