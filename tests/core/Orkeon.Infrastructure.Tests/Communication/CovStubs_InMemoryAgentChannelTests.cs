using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;
using Orkeon.Infrastructure.Communication;

namespace Orkeon.Infrastructure.Tests.CovStubs;

public sealed class CovStubs_InMemoryAgentChannelTests
{
    private static InMemoryAgentChannel CreateSut() => new(NullLogger<InMemoryAgentChannel>.Instance);

    [Fact]
    public void Constructor_NullLogger_Throws()
        => Assert.Throws<ArgumentNullException>(() => new InMemoryAgentChannel(null!));

    [Fact]
    public async Task Request_NullRequest_Throws()
    {
        var sut = CreateSut();
        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.RequestAsync(null!, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Request_NoHandler_ReturnsFailure()
    {
        var sut = CreateSut();
        var request = AgentChannelRequest.Create(AgentId.Create(), AgentId.Create(), "info", "ping");

        var response = await sut.RequestAsync(request, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(response.Success);
        Assert.Equal(request.CorrelationId, response.CorrelationId);
        Assert.Contains("No handler", response.Error);
    }

    [Fact]
    public async Task Request_WithHandler_ReturnsResponse()
    {
        var sut = CreateSut();
        var target = AgentId.Create();
        sut.RegisterHandler(target, (req, _) =>
            Task.FromResult(AgentChannelResponse.Ok(req.CorrelationId, target, "pong:" + req.Payload)));

        var request = AgentChannelRequest.Create(AgentId.Create(), target, "info", "ping");
        var response = await sut.RequestAsync(request, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(response.Success);
        Assert.Equal("pong:ping", response.Payload);
        Assert.Equal(request.CorrelationId, response.CorrelationId);
    }

    [Fact]
    public void RegisterHandler_NullArgs_Throw()
    {
        var sut = CreateSut();
        Assert.Throws<ArgumentNullException>(() => sut.RegisterHandler(null!, (_, _) => Task.FromResult(AgentChannelResponse.Ok(Guid.NewGuid(), AgentId.Create(), ""))));
        Assert.Throws<ArgumentNullException>(() => sut.RegisterHandler(AgentId.Create(), null!));
    }

    [Fact]
    public async Task Dispose_Registration_UnregistersHandler()
    {
        var sut = CreateSut();
        var target = AgentId.Create();
        var registration = sut.RegisterHandler(target, (req, _) =>
            Task.FromResult(AgentChannelResponse.Ok(req.CorrelationId, target, "ok")));

        registration.Dispose();

        var response = await sut.RequestAsync(AgentChannelRequest.Create(AgentId.Create(), target, "info", "x"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(response.Success);
    }

    [Fact]
    public async Task Request_HandlerExceedsTimeout_ThrowsTimeoutException()
    {
        var sut = CreateSut();
        var target = AgentId.Create();
        sut.RegisterHandler(target, async (req, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return AgentChannelResponse.Ok(req.CorrelationId, target, "never");
        });

        var request = AgentChannelRequest.Create(AgentId.Create(), target, "info", "x");

        await Assert.ThrowsAsync<TimeoutException>(
            () => sut.RequestAsync(request, timeout: TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Request_CallerCancellation_PropagatesOperationCanceled()
    {
        var sut = CreateSut();
        var target = AgentId.Create();
        sut.RegisterHandler(target, async (req, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return AgentChannelResponse.Ok(req.CorrelationId, target, "never");
        });

        using var cts = new CancellationTokenSource();
        var request = AgentChannelRequest.Create(AgentId.Create(), target, "info", "x");
        var task = sut.RequestAsync(request, timeout: TimeSpan.FromSeconds(30), cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task Broadcast_UnknownCrew_NoOp()
    {
        var sut = CreateSut();
        // A handler registered against a member of a *different* crew must never be invoked
        // when broadcasting to an unknown crew (the broadcast is a no-op, not a global fan-out).
        var unrelatedMember = AgentId.Create();
        var delivered = 0;
        sut.RegisterHandler(unrelatedMember, (req, _) =>
        {
            Interlocked.Increment(ref delivered);
            return Task.FromResult(AgentChannelResponse.Ok(req.CorrelationId, unrelatedMember, ""));
        });
        sut.RegisterCrewMember(CrewId.Create(), unrelatedMember);

        // Should not throw and should not deliver to anyone when the crew has no registered members.
        await sut.BroadcastAsync(AgentId.Create(), CrewId.Create(), "hello", TestContext.Current.CancellationToken);

        Assert.Equal(0, delivered);
    }

    [Fact]
    public async Task Broadcast_DeliversToOtherMembersExceptSender()
    {
        var sut = CreateSut();
        var crewId = CrewId.Create();
        var sender = AgentId.Create();
        var memberA = AgentId.Create();
        var memberB = AgentId.Create();

        var received = new System.Collections.Concurrent.ConcurrentBag<AgentId>();
        sut.RegisterHandler(sender, (req, _) => { received.Add(sender); return Task.FromResult(AgentChannelResponse.Ok(req.CorrelationId, sender, "")); });
        sut.RegisterHandler(memberA, (req, _) => { received.Add(memberA); return Task.FromResult(AgentChannelResponse.Ok(req.CorrelationId, memberA, "")); });
        sut.RegisterHandler(memberB, (req, _) => { received.Add(memberB); return Task.FromResult(AgentChannelResponse.Ok(req.CorrelationId, memberB, "")); });

        sut.RegisterCrewMember(crewId, sender);
        sut.RegisterCrewMember(crewId, memberA);
        sut.RegisterCrewMember(crewId, memberB);

        await sut.BroadcastAsync(sender, crewId, "announcement", TestContext.Current.CancellationToken);

        Assert.Contains(memberA, received);
        Assert.Contains(memberB, received);
        Assert.DoesNotContain(sender, received);
    }

    [Fact]
    public async Task Broadcast_SkipsMembersWithoutHandler()
    {
        var sut = CreateSut();
        var crewId = CrewId.Create();
        var sender = AgentId.Create();
        var withHandler = AgentId.Create();
        var withoutHandler = AgentId.Create();

        var delivered = 0;
        sut.RegisterHandler(withHandler, (req, _) =>
        {
            Interlocked.Increment(ref delivered);
            return Task.FromResult(AgentChannelResponse.Ok(req.CorrelationId, withHandler, ""));
        });

        sut.RegisterCrewMember(crewId, sender);
        sut.RegisterCrewMember(crewId, withHandler);
        sut.RegisterCrewMember(crewId, withoutHandler);

        await sut.BroadcastAsync(sender, crewId, "msg", TestContext.Current.CancellationToken);

        Assert.Equal(1, delivered);
    }
}
