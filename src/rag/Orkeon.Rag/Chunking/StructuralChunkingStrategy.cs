using Orkeon.Rag.Abstractions.Options;

namespace Orkeon.Rag.Chunking;

/// <summary>
/// Structure-aware chunking for Markdown-like text: splits content at heading
/// boundaries (lines starting with <c>#</c> through <c>######</c>), keeping each
/// heading with its body. A leading block before any heading is captured as its own
/// chunk. Sections exceeding <see cref="ChunkingOptions.MaxChunkSize"/> are sub-split
/// with the recursive separator hierarchy. Each chunk of a heading section carries
/// the heading text in its metadata under
/// <see cref="ChunkingStrategyBase.HeadingMetadataKey"/>.
/// </summary>
/// <remarks>
/// Canonical port of the historical <c>MdxSearchTool.ChunkByHeadings</c> heading-aware
/// chunker (RAG-02/C3 consolidation), extended with exact global offsets.
/// </remarks>
public sealed class StructuralChunkingStrategy : ChunkingStrategyBase
{
    /// <inheritdoc />
    public override string Name => "structural";

    internal override List<ChunkSlice> ComputeSlices(
        string content, int maxChunkSize, int overlap, ChunkingOptions options)
    {
        var sections = SplitSections(content);
        var slices = new List<ChunkSlice>();

        foreach (var section in sections)
        {
            var trimmedLength = TrimmedLength(content, section.Start, section.End);
            if (trimmedLength == 0)
                continue;

            if (trimmedLength <= maxChunkSize)
            {
                slices.Add(new ChunkSlice(section.Start, section.End, section.Heading));
            }
            else
            {
                // Sub-split the oversized section with the recursive hierarchy,
                // propagating the section heading to every sub-slice.
                var subSlices = new List<ChunkSlice>();
                RecursiveChunkingStrategy.SplitSpan(
                    content[section.Start..section.End],
                    section.Start,
                    RecursiveChunkingStrategy.DefaultSeparators,
                    0,
                    maxChunkSize,
                    overlap,
                    subSlices);

                foreach (var sub in subSlices)
                    slices.Add(sub with { Heading = section.Heading });
            }
        }

        return slices;
    }

    /// <summary>
    /// Splits content into heading-delimited sections: an optional preamble
    /// (before the first heading, <c>Heading == null</c>) followed by one section
    /// per heading line, each running until the next heading (or end of content).
    /// </summary>
    private static List<(int Start, int End, string? Heading)> SplitSections(string content)
    {
        var sections = new List<(int Start, int End, string? Heading)>();

        int sectionStart = 0;
        string? sectionHeading = null;
        bool hasOpenSection = false;

        int lineStart = 0;
        while (lineStart <= content.Length)
        {
            int newlineIdx = lineStart < content.Length
                ? content.IndexOf('\n', lineStart)
                : -1;
            int lineEnd = newlineIdx >= 0 ? newlineIdx : content.Length;

            if (TryGetHeading(content, lineStart, lineEnd, out var heading))
            {
                if (hasOpenSection && lineStart > sectionStart)
                    sections.Add((sectionStart, lineStart, sectionHeading));

                sectionStart = lineStart;
                sectionHeading = heading;
                hasOpenSection = true;
            }
            else if (!hasOpenSection)
            {
                hasOpenSection = true; // preamble before any heading
            }

            if (newlineIdx < 0)
                break;
            lineStart = newlineIdx + 1;
        }

        if (hasOpenSection && content.Length > sectionStart)
            sections.Add((sectionStart, content.Length, sectionHeading));

        return sections;
    }

    /// <summary>
    /// Returns <c>true</c> when the line <c>[lineStart, lineEnd)</c> is a Markdown
    /// heading: optional indentation, then 1–6 <c>#</c> characters followed by
    /// whitespace or the end of the line.
    /// </summary>
    private static bool TryGetHeading(string content, int lineStart, int lineEnd, out string? heading)
    {
        heading = null;

        int i = lineStart;
        while (i < lineEnd && (content[i] == ' ' || content[i] == '\t'))
            i++;

        int hashes = 0;
        while (i < lineEnd && content[i] == '#' && hashes <= 6)
        {
            hashes++;
            i++;
        }

        if (hashes is < 1 or > 6)
            return false;

        if (i < lineEnd && content[i] != ' ' && content[i] != '\t')
            return false;

        heading = content[lineStart..lineEnd].Trim();
        return heading.Length > 0;
    }

    private static int TrimmedLength(string content, int start, int end)
    {
        while (start < end && char.IsWhiteSpace(content[start]))
            start++;
        while (end > start && char.IsWhiteSpace(content[end - 1]))
            end--;
        return end - start;
    }
}
