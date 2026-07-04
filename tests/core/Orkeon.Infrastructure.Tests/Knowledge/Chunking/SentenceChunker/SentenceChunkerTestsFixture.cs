using Orkeon.Application.Interfaces.Knowledge;
using Orkeon.Infrastructure.Knowledge.Chunking;

namespace Orkeon.Infrastructure.Tests.Knowledge.Chunking;

public class SentenceChunkerTestsFixture
{
    private readonly SentenceChunker _chunker = new();

    public IReadOnlyList<TextChunk> Chunk(string text, ChunkingOptions? options = null)
        => options is not null ? _chunker.Chunk(text, options) : _chunker.Chunk(text);

    public SentenceChunker GetChunker() => _chunker;
}
