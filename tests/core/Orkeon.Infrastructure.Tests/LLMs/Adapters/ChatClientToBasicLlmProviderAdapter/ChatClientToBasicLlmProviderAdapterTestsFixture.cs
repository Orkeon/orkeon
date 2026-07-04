using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.Adapters;

public sealed class ChatClientToBasicLlmProviderAdapterTestsFixture : IDisposable
{
    private readonly MockChatClient _mockChatClient = new();
    private readonly MockLogger<ChatClientToBasicLlmProviderAdapter> _mockLogger = new();

    public ChatClientToBasicLlmProviderAdapterTestsFixture()
    {
    }

    public ChatClientToBasicLlmProviderAdapterTestsFixture WithMockChatClient(MockChatClient value)
    {
        // Configure _mockChatClient as needed
        return this;
    }

    public ChatClientToBasicLlmProviderAdapterTestsFixture WithMockLogger(MockLogger<ChatClientToBasicLlmProviderAdapter> value)
    {
        // Configure _mockLogger as needed
        return this;
    }

    public MockChatClient GetMockChatClient() => _mockChatClient;
    public MockLogger<ChatClientToBasicLlmProviderAdapter> GetMockLogger() => _mockLogger;


    public void Dispose()
    {
        _mockChatClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
