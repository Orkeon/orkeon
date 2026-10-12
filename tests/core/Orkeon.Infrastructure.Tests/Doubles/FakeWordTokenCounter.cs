using Orkeon.Domain.SharedKernel;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>Counts one token per whitespace-separated word.</summary>
public sealed class FakeWordTokenCounter : ITokenCounter
{
    /// <summary>The total number of characters given to <see cref="CountTokens"/>.</summary>
    public long CharactersCounted;

    public int CountTokens(string text)
    {
        CharactersCounted += text.Length;
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
