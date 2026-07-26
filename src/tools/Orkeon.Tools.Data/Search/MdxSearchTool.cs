using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Attributes;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Chunking;

namespace Orkeon.Tools.Data.Search;

/// <summary>
/// Tool for performing semantic search within Markdown (.md, .mdx) files.
/// Strips YAML frontmatter and JSX imports/exports before ingestion, then
/// delegates to the shared ephemeral-collection RAG search (RAG-03/C5) with the
/// canonical <c>structural</c> (heading-aware) chunking strategy, so each
/// section stays self-contained and an unchanged corpus is never re-embedded.
/// </summary>
[ToolContract("mdx_search",
    Name = "mdx_search",
    Description = "Perform semantic search within Markdown (.md, .mdx) files using RAG embeddings. Strips frontmatter and JSX before searching.",
    Category = "Search")]
public partial class MdxSearchTool : FileSearchToolBase<MdxSearchRequest>
{
    /// <inheritdoc />
    protected override IReadOnlyList<string> SupportedExtensions => [".md", ".mdx", ".markdown"];

    /// <inheritdoc />
    protected override string ToolCategory => "Markdown";

    /// <inheritdoc />
    protected override string ChunkingStrategyName => "structural";

    /// <summary>Initializes a new instance of <see cref="MdxSearchTool"/> with VFS support.</summary>
    /// <param name="searchService">Shared ephemeral-collection RAG search engine.</param>
    /// <param name="fileSystemService">Virtual file system service.</param>
    /// <param name="logger">Optional logger.</param>
    public MdxSearchTool(IEphemeralCollectionSearch searchService, IFileSystemService fileSystemService, ILogger<MdxSearchTool>? logger = null)
        : base(searchService, fileSystemService, logger) { }

    /// <summary>
    /// Strips YAML frontmatter, JSX imports, and JSX exports from Markdown/MDX content.
    /// </summary>
    protected override string PreprocessContent(string content)
    {
        // 1. Strip YAML frontmatter: ^---\s*\n[\s\S]*?\n---\s*\n
        content = FrontmatterRegex().Replace(content, "");

        // 2. Strip JSX imports: ^import\s+.*$
        content = JsxImportRegex().Replace(content, "");

        // 3. Strip JSX exports: ^export\s+(default\s+)?.*$
        content = JsxExportRegex().Replace(content, "");

        return content.Trim();
    }

    /// <summary>
    /// Splits Markdown content into heading-delimited sections via the canonical
    /// <see cref="StructuralChunkingStrategy"/> (RAG-02/C3 consolidation), mapping chunk
    /// offsets back to 1-based approximate source lines. Long sections are sub-split
    /// recursively by the strategy itself. Kept as the reference implementation of the
    /// tool's chunking behavior (the runtime path delegates to the same strategy by name).
    /// </summary>
    internal static List<(string Text, int ApproximateLine)> ChunkByHeadings(
        string content, int maxChunkSize)
    {
        var chunks = new StructuralChunkingStrategy().ChunkText(content, maxChunkSize);
        return [.. chunks.Select(c => (c.Content, PlainTextChunking.LineNumberAt(content, c.StartOffset)))];
    }

    [GeneratedRegex(@"^---\s*\n[\s\S]*?\n---\s*\n?", RegexOptions.Multiline)]
    private static partial Regex FrontmatterRegex();

    [GeneratedRegex(@"^import\s+.*$", RegexOptions.Multiline)]
    private static partial Regex JsxImportRegex();

    [GeneratedRegex(@"^export\s+(default\s+)?.*$", RegexOptions.Multiline)]
    private static partial Regex JsxExportRegex();
}
