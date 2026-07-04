using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.Adapters;

public class LlmProviderToChatClientAdapterTestsFixture
{
    private readonly MockLlmProvider _llmProvider = new();

    public LlmProviderToChatClientAdapterTestsFixture()
    {
    }

    public LlmProviderToChatClientAdapterTestsFixture WithLlmProvider(MockLlmProvider value)
    {
        // Configure _llmProvider as needed
        return this;
    }

    public MockLlmProvider GetLlmProvider() => _llmProvider;

}
