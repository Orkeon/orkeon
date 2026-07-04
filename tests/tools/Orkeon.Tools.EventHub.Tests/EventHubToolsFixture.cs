using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.EventHub;

namespace Orkeon.Tools.EventHub.Tests;

/// <summary>Fresh hub + caller context per test method (xUnit instantiates the test class per test).</summary>
public sealed class EventHubToolsFixture : IDisposable
{
    public DefaultEventHubCallerContext Caller { get; }
    public InMemoryEventHub Hub { get; }

    public PublishEventTool PublishEvent { get; }
    public PostMessageTool PostMessage { get; }
    public SendRequestTool SendRequest { get; }
    public ReplyToTool ReplyTo { get; }
    public ReceiveMessageTool ReceiveMessage { get; }
    public WaitForEventTool WaitForEvent { get; }
    public GetLastValueTool GetLastValue { get; }

    public EventHubToolsFixture()
    {
        Caller = new DefaultEventHubCallerContext();
        Hub = new InMemoryEventHub(Caller, NullLogger<InMemoryEventHub>.Instance);

        PublishEvent = new PublishEventTool(Hub);
        PostMessage = new PostMessageTool(Hub);
        SendRequest = new SendRequestTool(Hub);
        ReplyTo = new ReplyToTool(Hub);
        ReceiveMessage = new ReceiveMessageTool(Hub, Caller);
        WaitForEvent = new WaitForEventTool(Hub);
        GetLastValue = new GetLastValueTool(Hub, Caller);
    }

    public void Dispose() => Hub.Dispose();
}
