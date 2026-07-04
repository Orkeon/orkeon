using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.Flows;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.Flows;

public class FlowEngineTestsFixture
{
    private readonly MockServiceProvider _serviceProviderMock = new();
    private readonly ILogger<FlowEngine> _logger = NullLogger<FlowEngine>.Instance;

    public FlowEngineTestsFixture()
    {
    }

    public FlowEngineTestsFixture WithServiceProviderMock(MockServiceProvider value)
    {
        // Configure _serviceProviderMock as needed
        return this;
    }

    public FlowEngineTestsFixture WithLogger(ILogger<FlowEngine> value)
    {
        // Configure _logger as needed
        return this;
    }

    public MockServiceProvider GetServiceProviderMock() => _serviceProviderMock;
    public ILogger<FlowEngine> GetLogger() => _logger;

}
