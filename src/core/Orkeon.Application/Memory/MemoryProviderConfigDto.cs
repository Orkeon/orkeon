using static Orkeon.Domain.Constants.Memory.MemoryDefaults;

namespace Orkeon.Application.Memory;

/// <summary>
/// Application-layer DTO for memory provider configuration.
/// Distinct from the Domain's <c>Orkeon.Domain.Memory.MemoryProviderConfig</c> value object.
/// </summary>
public record MemoryProviderConfigDto(
    string Type,
    string ConnectionString,
    Dictionary<string, object>? Options = null);

/// <summary>
/// Gets common configurations for different memory providers.
/// </summary>
public static class MemoryProviderConfigDefaults
{
    private static readonly Lazy<MemoryProviderConfigDto> _inMemory =
        new(() => new MemoryProviderConfigDto(DefaultProvider, string.Empty));

    /// <summary>
    /// Gets or sets the in memory.
    /// </summary>
    public static MemoryProviderConfigDto InMemory => _inMemory.Value;

    /// <summary>
    /// Redis.
    /// </summary>
    public static MemoryProviderConfigDto Redis(string connectionString) =>
        new("Redis", connectionString);

    /// <summary>
    /// Chroma DB.
    /// </summary>
    public static MemoryProviderConfigDto ChromaDB(string endpoint, string apiKey) =>
        new("ChromaDB", endpoint, new Dictionary<string, object> { ["ApiKey"] = apiKey });
}
