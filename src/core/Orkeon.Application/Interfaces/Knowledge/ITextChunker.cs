namespace Orkeon.Application.Interfaces.Knowledge;

/// <summary>
/// Splits text into smaller chunks for processing and indexing.
/// </summary>
public interface ITextChunker
{
    /// <summary>
    /// Splits text into chunks based on the provided options.
    /// </summary>
    /// <param name="text">The text to chunk.</param>
    /// <param name="options">Optional chunking parameters.</param>
    /// <returns>A list of text chunks with position information.</returns>
    IReadOnlyList<TextChunk> Chunk(string text, ChunkingOptions? options = null);
}

/// <summary>
/// Represents a chunk of text with its position in the original document.
/// </summary>
public record TextChunk(
    string Content,
    int StartIndex,
    int EndIndex,
    Dictionary<string, object>? Metadata = null);

/// <summary>
/// Options for controlling text chunking behavior.
/// </summary>
public class ChunkingOptions
{
    /// <summary>
    /// Maximum number of characters per chunk.
    /// </summary>
    public int ChunkSize { get; set; } = 500;

    /// <summary>
    /// Number of overlapping characters between consecutive chunks.
    /// </summary>
    public int ChunkOverlap { get; set; } = 50;

    /// <summary>
    /// Custom separators to use for splitting, in order of priority.
    /// If null, the chunker uses its default separators.
    /// </summary>
    public IReadOnlyList<string>? Separators { get; init; }
}
