using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Memory;
using Orkeon.Domain.Attributes;

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
    /// Splits Markdown content into heading-delimited sections. Each section consists of the
    /// heading line followed by its body text. A leading block before any heading is also
    /// captured. Long sections are further split into paragraph-level sub-chunks.
    /// </summary>
    internal static List<(string Text, int ApproximateLine)> ChunkByHeadings(
        string content, int maxChunkSize)
    {
        var lines = content.Split('\n');
        var sections = new List<(StringBuilder Text, int StartLine)>();

        StringBuilder? currentSection = null;
        var currentStartLine = 1;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineNumber = i + 1;

            if (IsHeadingLine(line))
            {
                // Flush the previous section
                if (currentSection is not null && currentSection.Length > 0)
                {
                    sections.Add((currentSection, currentStartLine));
                }

                currentSection = new StringBuilder();
                currentSection.AppendLine(line);
                currentStartLine = lineNumber;
            }
            else
            {
                currentSection ??= new StringBuilder();
                currentSection.AppendLine(line);
            }
        }

        // Flush the last section
        if (currentSection is not null && currentSection.Length > 0)
        {
            sections.Add((currentSection, currentStartLine));
        }

        // Post-process: sub-split sections that exceed maxChunkSize
        var chunks = new List<(string Text, int ApproximateLine)>();

        foreach (var (sectionText, startLine) in sections)
        {
            var text = sectionText.ToString().Trim();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            if (text.Length <= maxChunkSize)
            {
                chunks.Add((text, startLine));
            }
            else
            {
                // Fall back to paragraph-level splitting within the section
                var subChunks = ChunkContent(text, maxChunkSize);
                foreach (var (subText, subApproxLine) in subChunks)
                {
                    // Offset the approximate line relative to the section start
                    chunks.Add((subText, startLine + subApproxLine - 1));
                }
            }
        }

        return chunks;
    }

    /// <summary>
    /// Returns <c>true</c> when the line is a Markdown heading (# through ######).
    /// </summary>
    private static bool IsHeadingLine(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '#')
            return false;

        // Count leading '#' characters (1-6)
        var hashCount = 0;
        foreach (var c in trimmed)
        {
            if (c == '#') hashCount++;
            else break;
        }

        // Must have 1-6 hashes followed by a space or end of line
        return hashCount >= 1 && hashCount <= 6 &&
               (trimmed.Length == hashCount || trimmed[hashCount] == ' ');
    }

    [GeneratedRegex(@"^---\s*\n[\s\S]*?\n---\s*\n?", RegexOptions.Multiline)]
    private static partial Regex FrontmatterRegex();

    [GeneratedRegex(@"^import\s+.*$", RegexOptions.Multiline)]
    private static partial Regex JsxImportRegex();

    [GeneratedRegex(@"^export\s+(default\s+)?.*$", RegexOptions.Multiline)]
    private static partial Regex JsxExportRegex();
}
