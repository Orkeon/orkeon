using System.Text.Json.Nodes;

namespace Orkeon.Application.EventHub;

/// <summary>
/// Stores JSON Schemas associated with <c>Message.SchemaId</c> values. v1.0 ships the port plus
/// an in-memory implementation; validation middleware arrives in v1.2.
/// </summary>
/// <remarks>
/// Ambiguity resolved: the spec references a <c>JsonSchema</c> type that does not exist in the
/// repository. <see cref="JsonNode"/> is used as the v1.0 representation — opaque, immutable in
/// practice, and standard-library only.
/// </remarks>
public interface IEventSchemaRegistry
{
    /// <summary>Fetches the schema registered under <paramref name="schemaId"/>, or <see langword="null"/> if absent.</summary>
    Task<JsonNode?> GetAsync(string schemaId, CancellationToken ct);

    /// <summary>
    /// Registers <paramref name="schema"/> under <paramref name="schemaId"/>. Rejects re-registration
    /// with a different schema for the same id.
    /// </summary>
    System.Threading.Tasks.Task RegisterAsync(string schemaId, JsonNode schema, CancellationToken ct);
}
