using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Tests.Chunking;

/// <summary>Shared helpers for the chunking strategy test suites.</summary>
internal static class ChunkingTestHelper
{
    /// <summary>Wraps <paramref name="content"/> in a test document.</summary>
    public static RagDocument Doc(string content, string id = "doc-1", string sourceId = "src-1") =>
        new()
        {
            Id = id,
            SourceId = sourceId,
            Content = content
        };

    /// <summary>
    /// Asserts the core offset invariant of all built-in strategies:
    /// <c>document.Content[StartOffset..EndOffset] == Content</c>, offsets in bounds,
    /// index sequential, and ids unique.
    /// </summary>
    public static void AssertOffsetsCoherent(RagDocument document, IReadOnlyList<Chunk> chunks)
    {
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];

            Assert.True(chunk.StartOffset >= 0, $"chunk {i}: StartOffset must be >= 0");
            Assert.True(chunk.EndOffset > chunk.StartOffset, $"chunk {i}: EndOffset must be > StartOffset");
            Assert.True(chunk.EndOffset <= document.Content.Length, $"chunk {i}: EndOffset out of bounds");
            Assert.Equal(
                document.Content[chunk.StartOffset..chunk.EndOffset],
                chunk.Content);
            Assert.Equal(i, chunk.Index);
            Assert.Equal(document.Id, chunk.DocumentId);
            Assert.Equal(document.SourceId, chunk.SourceId);
            Assert.True(seenIds.Add(chunk.Id), $"chunk id '{chunk.Id}' is not unique");
        }
    }
}
