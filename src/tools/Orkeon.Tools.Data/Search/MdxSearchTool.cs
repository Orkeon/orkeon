using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Memory;
using Orkeon.Domain.Attributes;
using Orkeon.Rag.Chunking;

namespace Orkeon.Tools.Data.Search;

/// <summary>
/// Tool for performing semantic search within Markdown (.md, .mdx) files using RAG embeddings.
/// Strips YAML frontmatter and JSX imports/exports before chunking, then uses heading-aware
/// chunking that splits content at ## boundaries so each section stays self-contained.
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

    /// <summary>Initializes a new instance of <see cref="MdxSearchTool"/> with VFS support.</summary>
    /// <param name="embeddingService">The embedding service used to vectorize text.</param>
    /// <param name="fileSystemService">Virtual file system service.</param>
    /// <param name="logger">Optional logger.</param>
    public MdxSearchTool(IEmbeddingService embeddingService, IFileSystemService fileSystemService, ILogger<MdxSearchTool>? logger = null)
        : base(embeddingService, fileSystemService, logger) { }

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
    /// Heading-aware chunking: splits Markdown content at heading boundaries (lines starting
    /// with # through ######). Each heading and its body become a separate chunk.  When a
    /// section exceeds <paramref name="maxChunkSize"/> it is further split by paragraphs.
    /// </summary>
    protected override IReadOnlyList<(string Text, int ApproximateLine)> ChunkContentVirtual(
        string content, int maxChunkSize)
    {
        ArgumentNullException.ThrowIfNull(content);
        return ChunkByHeadings(content, maxChunkSize);
    }

    /// <summary>
    /// Splits Markdown content into heading-delimited sections via the canonical
    /// <see cref="StructuralChunkingStrategy"/> (RAG-02/C3 consolidation), mapping chunk
    /// offsets back to 1-based approximate source lines. Long sections are sub-split
    /// recursively by the strategy itself.
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
