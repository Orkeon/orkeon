using Orkeon.Application.Rag;

namespace Orkeon.Application.Interfaces.Rag;

/// <summary>
/// Retrieves relevant chunks from knowledge sources for a given query.
/// </summary>
public interface IRetriever
{
    /// <summary>
    /// Retrieves ranked document chunks relevant to the query.
    /// </summary>
    System.Threading.Tasks.Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(
        string query, RetrievalOptions options, CancellationToken ct = default);
}
