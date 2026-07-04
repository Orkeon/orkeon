using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Memory;
using System.Collections.Concurrent;

namespace Orkeon.Infrastructure.Memory;

/// <summary>
/// Entity memory for tracking people, places, concepts.
/// </summary>
public sealed class EntityMemory : IEntityMemory
{
    private readonly ConcurrentDictionary<string, MemoryEntity> _entities = new();

    /// <inheritdoc />
    public Task AddEntityAsync(
        string entityName,
        EntityType type,
        Dictionary<string, string> attributes)
    {
        ArgumentNullException.ThrowIfNull(entityName);
        var entity = MemoryEntity.Create(
            entityName,
            type,
            attributes);

#pragma warning disable CA1308 // lowercase is the required dictionary-key storage form, not a comparison normalization
        _entities[entityName.ToLowerInvariant()] = entity;
#pragma warning restore CA1308

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<MemoryEntity?> GetEntityAsync(string entityName)
    {
        ArgumentNullException.ThrowIfNull(entityName);
#pragma warning disable CA1308 // lowercase is the required dictionary-key lookup form matching the stored key, not a comparison normalization
        _entities.TryGetValue(entityName.ToLowerInvariant(), out var entity);
#pragma warning restore CA1308
        return Task.FromResult(entity);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MemoryEntity>> GetEntitiesByTypeAsync(EntityType type)
    {
        var entities = _entities.Values
            .Where(e => e.Type == type)
            .OrderBy(e => e.Name)
            .ToList();

        return Task.FromResult<IReadOnlyList<MemoryEntity>>(entities);
    }

    /// <inheritdoc />
    public Task UpdateEntityAsync(string entityName, Dictionary<string, string> attributes)
    {
        ArgumentNullException.ThrowIfNull(entityName);
#pragma warning disable CA1308 // lowercase is the required dictionary-key storage form, not a comparison normalization
        var key = entityName.ToLowerInvariant();
#pragma warning restore CA1308

        // Use AddOrUpdate for thread-safe atomic operation
        _entities.AddOrUpdate(
            key,
            // If entity doesn't exist, create a new one
            k => MemoryEntity.Create(entityName, EntityType.Other, attributes),
            // If entity exists, merge attributes atomically
            (k, existingEntity) =>
            {
                var mergedAttributes = new Dictionary<string, string>(existingEntity.Attributes);
                foreach (var (attrKey, value) in attributes)
                {
                    mergedAttributes[attrKey] = value;
                }

                return existingEntity with
                {
                    Attributes = mergedAttributes,
                    LastUpdated = DateTime.UtcNow
                };
            });

        return Task.CompletedTask;
    }
}
