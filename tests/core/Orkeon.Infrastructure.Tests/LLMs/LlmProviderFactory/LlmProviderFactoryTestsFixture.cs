namespace Orkeon.Infrastructure.Tests.LLMs;

public class LlmProviderFactoryTestsFixture
{
    private readonly Dictionary<string, HttpClient> _clients = [];

    public LlmProviderFactoryTestsFixture()
    {
    }

    public Dictionary<string, HttpClient> GetClients() => _clients;

}
