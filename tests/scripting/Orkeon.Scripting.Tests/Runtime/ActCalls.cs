using System.Runtime.ExceptionServices;
using Jint;
using Jint.Native;
using Jint.Runtime;
using Orkeon.Scripting.Internal;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// Calls <c>ctx.llm.act</c> the way a script does — through its JS function — from a test that
/// owns an engine at rest, so the test thread is the only drainer.
/// </summary>
/// <remarks>
/// <c>act</c> is a JS async function (SCR-25: its <c>onDelta</c> callback is driven by a JS pump,
/// never from a CLR continuation), so a C# test no longer has a delegate to call: it invokes the
/// function and settles the promise. A rejection carrying a CLR exception is rethrown as that
/// exception, the way <c>JsCrew.UnwrapPromise</c> surfaces it to a host: the loop's own faulted
/// Task (its aggregate unwrapped), or Jint's <see cref="ExecutionCanceledException"/> for a
/// cancelled one — which the root pumps, not this helper, turn into the host's
/// <see cref="OperationCanceledException"/>.
/// </remarks>
internal static class ActCalls
{
    public static async Task<JsValue> ActAsync(this JsLlmFacade facade, Engine engine, string prompt, JsValue? options = null)
    {
        var promise = engine.Invoke(facade.act, prompt, options ?? JsValue.Undefined);
        try
        {
            return await promise.UnwrapIfPromiseAsync(TestContext.Current.CancellationToken);
        }
        catch (PromiseRejectedException ex) when (JsHostError.Unwrap(ex.RejectedValue) is { } clr)
        {
            ExceptionDispatchInfo.Capture(clr is AggregateException { InnerExceptions.Count: 1 } agg ? agg.InnerExceptions[0] : clr).Throw();
            throw;
        }
    }
}
