using Orkeon.Application.Interfaces.Knowledge;
using Orkeon.Infrastructure.Knowledge.Chunking;

namespace Orkeon.Infrastructure.Tests.Knowledge.Chunking;

public class RecursiveTextChunkerTestsFixture
{
    private readonly RecursiveTextChunker _chunker = new();

    public IReadOnlyList<TextChunk> Chunk(string text, ChunkingOptions? options = null)
        => options is not null ? _chunker.Chunk(text, options) : _chunker.Chunk(text);

    public RecursiveTextChunker GetChunker() => _chunker;
}
