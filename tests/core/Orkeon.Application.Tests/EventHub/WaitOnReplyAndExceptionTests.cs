using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.EventHub;
using Orkeon.Application.EventHub.Exceptions;
using Orkeon.Infrastructure.EventHub;
using Orkeon.Infrastructure.EventHub.DependencyInjection;

namespace Orkeon.Application.Tests.EventHub;

public sealed class WaitOnReplyAndExceptionTests
{
    private static InMemoryEventHub NewHub() => new(new DefaultEventHubCallerContext(), NullLogger<InMemoryEventHub>.Instance);

    [Fact]
    public async System.Threading.Tasks.Task WaitFor_OnReply_resolves_when_ReplyAsync_completes_the_correlation()
    {
        using var hub = NewHub();
        var correlation = CorrelationId.NewId();

        var waitTask = hub.WaitForAsync(
            new WaitOnReply(correlation),
            FiniteWaitTimeout.Of(TimeSpan.FromSeconds(2)),
            CancellationToken.None);

        await System.Threading.Tasks.Task.Yield();
        await hub.ReplyAsync(correlation, new { value = 99 }, CancellationToken.None);

        var msg = await waitTask;
        Assert.Equal(correlation.Value, msg.CorrelationId?.Value);
    }

    [Fact]
    public async System.Threading.Tasks.Task WaitFor_OnReply_with_Finite_expired_returns_WaitTimedOut()
    {
        using var hub = NewHub();
        var correlation = CorrelationId.NewId();

        var msg = await hub.WaitForAsync(
            new WaitOnReply(correlation),
            FiniteWaitTimeout.Of(TimeSpan.FromMilliseconds(80)),
            CancellationToken.None);

        Assert.Equal("_system.wait_timed_out", msg.Topic);
    }

    [Fact]
    public void ReservedTopicException_carries_topic_and_message()
    {
        var ex = new ReservedTopicException("_system.foo");
        Assert.Equal("_system.foo", ex.Topic);
        Assert.Contains("_system.foo", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidMailboxAddressException_with_paramName_is_constructible()
    {
        var ex = new InvalidMailboxAddressException("bad format", "uri");
        Assert.Equal("uri", ex.ParamName);
    }

    [Fact]
    public void AddOrkeonInMemoryEventHub_registers_all_three_services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOrkeonInMemoryEventHub();
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IEventHubCallerContext>());
        Assert.NotNull(provider.GetService<IEventSchemaRegistry>());
        Assert.NotNull(provider.GetService<IEventHub>());
    }

    [Fact]
    public void AddOrkeonInMemoryEventHub_is_idempotent()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOrkeonInMemoryEventHub();
        services.AddOrkeonInMemoryEventHub();
        using var provider = services.BuildServiceProvider();
        // Singleton resolves once.
        var hub1 = provider.GetRequiredService<IEventHub>();
        var hub2 = provider.GetRequiredService<IEventHub>();
        Assert.Same(hub1, hub2);
    }

    [Fact]
    public void MessageId_From_with_empty_throws()
        => Assert.Throws<ArgumentException>(() => MessageId.From(""));

    [Fact]
    public void CorrelationId_From_with_empty_throws()
        => Assert.Throws<ArgumentException>(() => CorrelationId.From(""));

    [Fact]
    public void MessageId_From_with_non_ulid_string_throws()
        => Assert.Throws<ArgumentException>(() => MessageId.From("not-a-ulid"));

    [Fact]
    public void CorrelationId_From_with_non_ulid_string_throws()
        => Assert.Throws<ArgumentException>(() => CorrelationId.From("not-a-ulid"));

    [Fact]
    public void MessageId_From_ulid_overload_round_trips()
    {
        var u = Ulid.NewUlid();
        var id = MessageId.From(u);
        Assert.Equal(u, id.Value);
        Assert.Equal(u.ToString(), id.AsString());
    }

    [Fact]
    public void CorrelationId_NewId_produces_a_valid_ulid()
        => Assert.NotEqual(Ulid.Empty, CorrelationId.NewId().Value);

    [Fact]
    public void WaitTimeout_Finite_Of_with_zero_throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => FiniteWaitTimeout.Of(TimeSpan.Zero));

    [Fact]
    public void CrewId_IsSystem_returns_false_for_user_crew()
        => Assert.False(Domain.Common.CrewId.IsSystem(Domain.Common.CrewId.Create()));

    [Fact]
    public void CrewId_IsSystem_returns_true_for_System_sentinel()
        => Assert.True(Domain.Common.CrewId.IsSystem(Domain.Common.CrewId.System));
}
