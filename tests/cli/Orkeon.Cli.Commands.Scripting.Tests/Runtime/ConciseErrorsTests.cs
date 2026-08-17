using System.Net.Http;
using System.Net.Sockets;
using Jint;
using Jint.Runtime;
using Orkeon.Cli.Commands.Scripting.Runtime;

namespace Orkeon.Cli.Commands.Scripting.Tests.Runtime;

/// <summary>
/// <see cref="ConciseErrors"/>: the transcript gets one actionable line, never a stringified
/// stack. Pinned on the live <c>/analyze</c> incident shape — a crew failure travelling as
/// PromiseRejectedException(ObjectWrapper(AggregateException(HttpRequestException(SocketException)))).
/// </summary>
public sealed class ConciseErrorsTests
{
    private static AggregateException TransportFailure()
        => new(new HttpRequestException(
            "Resource temporarily unavailable (api.moonshot.ai:443)",
            new SocketException(11)));

    [Fact]
    public void A_js_rejection_carrying_a_clr_exception_reads_as_its_root_cause()
    {
        using var engine = new Engine();
        engine.SetValue("boom", TransportFailure());
        var promise = engine.Evaluate("Promise.reject(boom)");

        var rejected = Assert.Throws<PromiseRejectedException>(() => promise.UnwrapIfPromise(TestContext.Current.CancellationToken));
        // The raw message is the stringified exception (stack included when thrown live) —
        // what the transcript used to show verbatim.
        Assert.StartsWith("Promise was rejected with value System.AggregateException", rejected.Message, StringComparison.Ordinal);

        Assert.Equal(
            "Resource temporarily unavailable (api.moonshot.ai:443)",
            ConciseErrors.Message(rejected));
    }

    [Fact]
    public void A_script_authored_rejection_shows_the_value_without_the_wrapper_sentence()
    {
        using var engine = new Engine();
        var promise = engine.Evaluate("Promise.reject('quota exceeded\\nsecond line')");

        var rejected = Assert.Throws<PromiseRejectedException>(() => promise.UnwrapIfPromise(TestContext.Current.CancellationToken));

        Assert.Equal("quota exceeded", ConciseErrors.Message(rejected));
    }

    [Fact]
    public void An_aggregate_exception_is_flattened_to_its_first_root_cause()
    {
        Assert.Equal(
            "Resource temporarily unavailable (api.moonshot.ai:443)",
            ConciseErrors.Message(TransportFailure()));
    }

    [Fact]
    public void A_plain_exception_passes_through_first_line_only()
    {
        var ex = new InvalidOperationException("first line\n   at Some.Stack.Frame()");
        Assert.Equal("first line", ConciseErrors.Message(ex));
    }
}
