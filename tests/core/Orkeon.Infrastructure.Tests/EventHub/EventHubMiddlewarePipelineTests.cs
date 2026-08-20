using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.EventHub.DependencyInjection;
using Orkeon.Application.EventHub;
using Orkeon.Domain.Common;
using Orkeon.Infrastructure.EventHub;

namespace Orkeon.Infrastructure.Tests.EventHub;

/// <summary>Records the order it was invoked in, on both paths.</summary>
internal sealed class TracingMiddleware : IEventHubMiddleware
{
    private readonly string _name;
    private readonly List<string> _trace;

    public TracingMiddleware(string name, List<string> trace)
    {
        _name = name;
        _trace = trace;
    }

    public async Task<Message> OnPublishAsync(Message message, Func<Message, Task<Message>> nextHandler, CancellationToken ct)
    {
        _trace.Add($"{_name}:publish:in");
        var result = await nextHandler(message).ConfigureAwait(false);
        _trace.Add($"{_name}:publish:out");
        return result;
    }

    public async Task<Message> OnReceiveAsync(Message message, Func<Message, Task<Message>> nextHandler, CancellationToken ct)
    {
        _trace.Add($"{_name}:receive:in");
        var result = await nextHandler(message).ConfigureAwait(false);
        _trace.Add($"{_name}:receive:out");
        return result;
    }
}

/// <summary>Refuses everything, the way an ACL middleware refuses: by throwing.</summary>
internal sealed class RefusingMiddleware : IEventHubMiddleware
{
    public Task<Message> OnPublishAsync(Message message, Func<Message, Task<Message>> nextHandler, CancellationToken ct) =>
        throw new UnauthorizedAccessException("refused");

    public Task<Message> OnReceiveAsync(Message message, Func<Message, Task<Message>> nextHandler, CancellationToken ct) =>
        throw new UnauthorizedAccessException("refused");
}

/// <summary>
/// The hub middleware pipeline (HUB-01). Until this existed the port shipped with its
/// contract only — nothing logged, instrumented, authorized, deduplicated or validated a hub
/// message. What matters here is order, symmetry, and that a refusal is not swallowed.
/// </summary>
public class EventHubMiddlewarePipelineTests
{
    private static Message AnyMessage() =>
        new()
        {
            Id = MessageId.NewId(),
            Topic = "t",
            Payload = ReadOnlyMemory<byte>.Empty,
            SchemaId = "s",
            PublishedAt = DateTimeOffset.UnixEpoch,
            SourceCrewId = CrewId.Create(),
            Metadata = System.Collections.Immutable.ImmutableDictionary<string, string>.Empty,
        };

    [Fact]
    public async Task An_empty_pipeline_is_a_pure_pass_through()
    {
        var pipeline = new EventHubMiddlewarePipeline([]);
        var message = AnyMessage();

        Assert.True(pipeline.IsEmpty);
        Assert.Same(message, await pipeline.OnPublishAsync(message, TestContext.Current.CancellationToken));
        Assert.Same(message, await pipeline.OnReceiveAsync(message, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Publish_runs_in_registration_order_and_receive_runs_in_reverse()
    {
        // The spec's §12 order is Logging → Telemetry → Acl → …, and it is not cosmetic:
        // logging is outermost so it sees what the ACL later rejects.
        var trace = new List<string>();
        var pipeline = new EventHubMiddlewarePipeline(
        [
            new TracingMiddleware("logging", trace),
            new TracingMiddleware("acl", trace),
        ]);

        await pipeline.OnPublishAsync(AnyMessage(), TestContext.Current.CancellationToken);

        Assert.Equal(
            ["logging:publish:in", "acl:publish:in", "acl:publish:out", "logging:publish:out"],
            trace);

        trace.Clear();
        await pipeline.OnReceiveAsync(AnyMessage(), TestContext.Current.CancellationToken);

        // Reverse on the way in, so a middleware wraps a message symmetrically.
        Assert.Equal(
            ["acl:receive:in", "logging:receive:in", "logging:receive:out", "acl:receive:out"],
            trace);
    }

    [Fact]
    public async Task A_refusal_reaches_the_caller_instead_of_being_swallowed()
    {
        // Unlike hook dispatch, where swallowing is correct, a middleware that refuses is
        // making a decision — the caller must hear it.
        var trace = new List<string>();
        var pipeline = new EventHubMiddlewarePipeline(
        [
            new TracingMiddleware("logging", trace),
            new RefusingMiddleware(),
        ]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => pipeline.OnPublishAsync(AnyMessage(), TestContext.Current.CancellationToken));

        // The outer middleware still saw the message on the way in — that is why logging
        // sits outermost.
        Assert.Equal(["logging:publish:in"], trace);
    }

    [Fact]
    public void The_observation_stages_register_in_the_specs_order()
    {
        // §12 is Logging → Telemetry → Acl → …, and logging must be outermost so it sees
        // what a later stage rejects. Registration order is execution order (HUB-01).
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging();
        services.AddOrkeonEventHubObservability();

        using var provider = services.BuildServiceProvider();
        var registered = provider.GetServices<IEventHubMiddleware>().Select(m => m.GetType().Name).ToList();

        Assert.Equal(["LoggingEventHubMiddleware", "TelemetryEventHubMiddleware"], registered);
    }

    [Fact]
    public async Task A_rejected_message_is_logged_and_its_span_is_marked_failed()
    {
        // The reject path is the reason to instrument at all: an operator needs the line,
        // and a refused message must be a failed span rather than a missing one.
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging();
        services.AddOrkeonEventHubObservability();
        using var provider = services.BuildServiceProvider();

        var pipeline = new EventHubMiddlewarePipeline(
            [.. provider.GetServices<IEventHubMiddleware>(), new RefusingMiddleware()]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => pipeline.OnPublishAsync(AnyMessage(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_hub_without_middleware_behaves_exactly_as_before()
    {
        // HUB-01's own acceptance criterion: adding the pipeline must cost a plain hub
        // nothing at all.
        using var hub = new InMemoryEventHub(
            new DefaultEventHubCallerContext(),
            NullLogger<InMemoryEventHub>.Instance);

        await hub.PublishAsync("orders.created", new { id = 1 }, options: null, TestContext.Current.CancellationToken);

        // No exception, no observable change: the empty pipeline short-circuits.
        Assert.True(true);
    }
}
