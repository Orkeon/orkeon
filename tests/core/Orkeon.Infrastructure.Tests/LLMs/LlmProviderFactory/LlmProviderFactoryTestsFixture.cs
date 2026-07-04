#pragma warning disable CS0618 // Testing obsolete APIs

namespace Orkeon.Infrastructure.Tests.LLMs;

public class LlmProviderFactoryTestsFixture
{
    private readonly Dictionary<string, HttpClient> _clients = [];

    public LlmProviderFactoryTestsFixture()
    {
    }

    public Dictionary<string, HttpClient> GetClients() => _clients;

#pragma warning restore CS0618
}
