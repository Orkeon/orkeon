using Orkeon.Infrastructure.Crew;

namespace Orkeon.Infrastructure.Tests.Managers;

public class LlmBasedManagerTestsFixture
{
    private readonly TestLlmProvider _llmProvider;
    private readonly TestLogger<LlmBasedManager> _logger;
    private readonly LlmBasedManager _manager;

    public LlmBasedManagerTestsFixture()
    {
        _llmProvider = new TestLlmProvider();
        _logger = new TestLogger<LlmBasedManager>();
        _manager = new LlmBasedManager(_logger, _llmProvider);
    }

    public LlmBasedManagerTestsFixture WithLlmProvider(TestLlmProvider value)
    {
        // Configure _llmProvider as needed
        return this;
    }

    public LlmBasedManagerTestsFixture WithLogger(TestLogger<LlmBasedManager> value)
    {
        // Configure _logger as needed
        return this;
    }

    public TestLlmProvider GetLlmProvider() => _llmProvider;
    public TestLogger<LlmBasedManager> GetLogger() => _logger;
    public LlmBasedManager GetManager() => _manager;

}
