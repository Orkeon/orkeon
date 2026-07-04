using Orkeon.Domain.SharedKernel;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>
/// Simple token counter implementation.
/// Uses approximation based on word count for MVP.
/// </summary>
public sealed class SimpleTokenCounter : ITokenCounter
{
    private const double TokensPerWord = 1.3; // Approximation
    private static readonly char[] s_whitespaceChars = [' ', '\t', '\n', '\r'];

    /// <inheritdoc />
    public int CountTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        // Simple approximation: split by whitespace and multiply by average tokens per word
        var words = text.Split(s_whitespaceChars, StringSplitOptions.RemoveEmptyEntries);
        return (int)(words.Length * TokensPerWord);
    }
}
