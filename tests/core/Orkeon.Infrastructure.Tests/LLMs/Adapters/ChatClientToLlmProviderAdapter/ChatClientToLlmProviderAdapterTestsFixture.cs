using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.Adapters;

public sealed class ChatClientToLlmProviderAdapterTestsFixture : IDisposable
{
    private readonly MockChatClient _mockChatClient = new();
    private readonly MockLogger<ChatClientToLlmProviderAdapter> _mockLogger = new();

    public ChatClientToLlmProviderAdapterTestsFixture()
    {
    }

    public ChatClientToLlmProviderAdapterTestsFixture WithMockChatClient(MockChatClient value)
    {
        // Configure _mockChatClient as needed
        return this;
    }

    public ChatClientToLlmProviderAdapterTestsFixture WithMockLogger(MockLogger<ChatClientToLlmProviderAdapter> value)
    {
        // Configure _mockLogger as needed
        return this;
    }

    public MockChatClient GetMockChatClient() => _mockChatClient;
    public MockLogger<ChatClientToLlmProviderAdapter> GetMockLogger() => _mockLogger;


    public void Dispose()
    {
        _mockChatClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
