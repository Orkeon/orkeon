using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Orkeon.Application.EventHub;

namespace Orkeon.Infrastructure.EventHub;

/// <summary>
/// In-memory <see cref="IEventSchemaRegistry"/> using a <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// Rejects re-registration of the same id with a structurally different schema.
/// </summary>
public sealed class InMemoryEventSchemaRegistry : IEventSchemaRegistry
{
    private readonly ConcurrentDictionary<string, JsonNode> _schemas = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public Task<JsonNode?> GetAsync(string schemaId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaId);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_schemas.TryGetValue(schemaId, out var schema) ? schema : null);
    }

    /// <inheritdoc/>
    public System.Threading.Tasks.Task RegisterAsync(string schemaId, JsonNode schema, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaId);
        ArgumentNullException.ThrowIfNull(schema);
        ct.ThrowIfCancellationRequested();

        var existing = _schemas.GetOrAdd(schemaId, schema);
        if (!ReferenceEquals(existing, schema) && !AreEquivalent(existing, schema))
        {
            throw new InvalidOperationException(
                $"Schema '{schemaId}' is already registered with a different definition.");
        }
        return System.Threading.Tasks.Task.CompletedTask;
    }

    private static bool AreEquivalent(JsonNode left, JsonNode right)
        => string.Equals(left.ToJsonString(), right.ToJsonString(), StringComparison.Ordinal);
}
