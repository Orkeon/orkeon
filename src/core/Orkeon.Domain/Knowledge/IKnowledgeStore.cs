namespace Orkeon.Domain.Knowledge;

/// <summary>
/// Interface for knowledge storage and retrieval.
/// </summary>
public interface IKnowledgeStore
{
    /// <summary>
    /// Stores knowledge in the store.
    /// </summary>
    Task<bool> StoreAsync(string key, object knowledge, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves knowledge from the store.
    /// </summary>
    Task<T?> RetrieveAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Searches for knowledge based on a query.
    /// </summary>
    Task<List<T>> SearchAsync<T>(string query, int maxResults = 10, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Deletes knowledge from the store.
    /// </summary>
    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if knowledge exists in the store.
    /// </summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
}
