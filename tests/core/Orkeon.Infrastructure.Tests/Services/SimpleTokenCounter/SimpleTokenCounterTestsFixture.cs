using Orkeon.Infrastructure.LLMs;

namespace Orkeon.Infrastructure.Tests.Services;

public class SimpleTokenCounterTestsFixture
{
    private readonly SimpleTokenCounter _tokenCounter;

    public SimpleTokenCounterTestsFixture()
    {
        _tokenCounter = new SimpleTokenCounter();
    }

    public int CountTokens(string text) => _tokenCounter.CountTokens(text);

    public SimpleTokenCounter GetTokenCounter() => _tokenCounter;

}
