using Orkeon.Infrastructure.Communication;

namespace Orkeon.Infrastructure.Tests.Communication;

public sealed class AsyncAgentCommunicationServiceTestsFixture : IDisposable
{
    private readonly AsyncAgentCommunicationService _service;

    public AsyncAgentCommunicationServiceTestsFixture()
    {
        _service = new AsyncAgentCommunicationService();
    }

    public AsyncAgentCommunicationService GetService() => _service;


    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }
}
