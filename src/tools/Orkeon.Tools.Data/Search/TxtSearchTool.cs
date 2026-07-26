using Microsoft.Extensions.Logging;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Attributes;
using Orkeon.Rag.Abstractions.Interfaces;

namespace Orkeon.Tools.Data.Search;

/// <summary>
/// Tool for performing semantic search within plain text (.txt) files.
/// Thin façade over the shared ephemeral-collection RAG search (RAG-03/C5):
/// files are ingested incrementally (unchanged corpus = zero embeddings) and
/// queried through the document store.
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
    /// <param name="searchService">Shared ephemeral-collection RAG search engine.</param>
    /// <param name="fileSystemService">Virtual file system service.</param>
    /// <param name="logger">Optional logger.</param>
    public TxtSearchTool(IEphemeralCollectionSearch searchService, IFileSystemService fileSystemService, ILogger<TxtSearchTool>? logger = null)
        : base(searchService, fileSystemService, logger) { }
}
