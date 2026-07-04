using Microsoft.Extensions.Logging;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Memory;
using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.Data.Search;

/// <summary>
/// Tool for performing semantic search within plain text (.txt) files using RAG embeddings.
/// Reads files, chunks content by paragraphs, embeds chunks and query, then returns
/// the most similar chunks ranked by cosine similarity.
/// </summary>
[ToolContract("txt_search",
    Name = "txt_search",
    Description = "Perform semantic search within plain text (.txt) files using RAG embeddings.",
    Category = "Search")]
// S101: "TXT" is a file-format acronym; this public type is referenced by the test project
// and consumers, so the name is kept stable intentionally.
public partial class TxtSearchTool : FileSearchToolBase<TxtSearchRequest>
{
    /// <inheritdoc />
    protected override IReadOnlyList<string> SupportedExtensions => [".txt"];

    /// <inheritdoc />
    protected override string ToolCategory => "Plain Text";

    /// <summary>Initializes a new instance of <see cref="TxtSearchTool"/> with VFS support.</summary>
    /// <param name="embeddingService">The embedding service used to vectorize text.</param>
    /// <param name="fileSystemService">Virtual file system service.</param>
    /// <param name="logger">Optional logger.</param>
    public TxtSearchTool(IEmbeddingService embeddingService, IFileSystemService fileSystemService, ILogger<TxtSearchTool>? logger = null)
        : base(embeddingService, fileSystemService, logger) { }
}
