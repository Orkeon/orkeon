using System.Text;

namespace Orkeon.Tools.Abstractions.Helpers;

/// <summary>
/// Shared helper that splits arbitrary text into RAG-friendly chunks.
/// Strategy: split on paragraph breaks first, then re-split paragraphs that
/// exceed the configured maximum size by sentence boundaries.
/// </summary>
public static class TextChunker
{
    /// <summary>
    /// Chunks <paramref name="content"/> into passages of at most
    /// <paramref name="maxChunkSize"/> characters. Each chunk carries the
    /// approximate source line where the passage starts.
    /// </summary>
    public static IReadOnlyList<(string Text, int ApproximateLine)> ChunkContent(string content, int maxChunkSize)
    {
        if (string.IsNullOrWhiteSpace(content))
            return [];

        var paragraphs = content.Split(["\n\n", "\r\n\r\n"], StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<(string Text, int ApproximateLine)>();
        var currentLine = 1;

        foreach (var paragraph in paragraphs)
        {
            var trimmed = paragraph.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                currentLine += CountLines(paragraph) + 2;
                continue;
            }

            if (trimmed.Length <= maxChunkSize)
            {
                chunks.Add((trimmed, currentLine));
            }
            else
            {
                SplitLargeParagraph(trimmed, maxChunkSize, currentLine, chunks);
            }

            currentLine += CountLines(paragraph) + 2;
        }

        return chunks;
    }

    private static void SplitLargeParagraph(
        string trimmed,
        int maxChunkSize,
        int currentLine,
        List<(string Text, int ApproximateLine)> chunks)
    {
        var sentences = SplitBySentences(trimmed);
        var buffer = new StringBuilder();
        var chunkStartLine = currentLine;

        foreach (var sentence in sentences)
        {
            if (buffer.Length + sentence.Length > maxChunkSize && buffer.Length > 0)
            {
                chunks.Add((buffer.ToString().Trim(), chunkStartLine));
                buffer.Clear();
                chunkStartLine = currentLine + CountLines(buffer.ToString());
            }
            buffer.Append(sentence);
        }

        if (buffer.Length > 0)
        {
            chunks.Add((buffer.ToString().Trim(), chunkStartLine));
        }
    }

    /// <summary>
    /// Computes cosine similarity between two equal-length embedding vectors.
    /// Returns 0 when either vector has zero norm.
    /// </summary>
    public static float CosineSimilarity(float[] a, float[] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Length != b.Length)
            throw new ArgumentException("Vectors must have the same length", nameof(b));

        float dot = 0f, na = 0f, nb = 0f;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }

        var denom = MathF.Sqrt(na) * MathF.Sqrt(nb);
        return denom > 0 ? dot / denom : 0f;
    }

    private static string[] SplitBySentences(string text)
    {
        var result = new List<string>();
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if ((text[i] == '.' || text[i] == '!' || text[i] == '?') &&
                (i + 1 >= text.Length || text[i + 1] == ' ' || text[i + 1] == '\n'))
            {
                result.Add(text.Substring(start, i - start + 1));
                start = i + 1;
            }
        }

        if (start < text.Length)
            result.Add(text[start..]);

        return result.ToArray();
    }

    private static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var count = 1;
        foreach (var c in text)
        {
            if (c == '\n') count++;
        }
        return count;
    }
}
