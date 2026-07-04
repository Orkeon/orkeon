namespace Orkeon.Domain.SharedKernel;

/// <summary>
/// Interface for counting tokens in text.
/// </summary>
public interface ITokenCounter
{
    /// <summary>
    /// Counts the number of tokens in the given text.
    /// </summary>
    /// <param name="text">The text to count tokens in.</param>
    /// <returns>The number of tokens.</returns>
    int CountTokens(string text);
}
