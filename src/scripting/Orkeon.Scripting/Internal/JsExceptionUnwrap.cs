namespace Orkeon.Scripting.Internal;

/// <summary>
/// Peels Jint promise / Aggregate wrappers off a thrown exception so callers
/// see the actionable inner message (e.g. <c>"tools.fileWrite failed: FQN=...
/// not found"</c>) instead of <c>"Promise was rejected with value
/// System.AggregateException..."</c>.
/// <para>
/// The Jint type names are matched by string so this helper does not pull
/// <c>Jint.Runtime</c> into the public surface of consumers (in particular
/// <c>Orkeon.Scripting.Cli</c>, which deliberately keeps its dependency on the
/// JS engine type-system narrow).
/// </para>
/// </summary>
internal static class JsExceptionUnwrap
{
    /// <summary>
    /// Maximum wrap depth peeled before giving up. The natural depth is
    /// <c>PromiseRejectedException → AggregateException → real</c> (≤ 3);
    /// 16 is a generous ceiling that still terminates if a pathological
    /// cycle exists.
    /// </summary>
    private const int MaxDepth = 16;

    internal static Exception UnwrapToInnermost(Exception ex)
    {
        var current = ex;
        for (var i = 0; i < MaxDepth && current is not null; i++)
        {
            if (current is AggregateException agg && agg.InnerExceptions.Count >= 1)
            {
                current = agg.InnerExceptions[0];
                continue;
            }
            var typeName = current.GetType().FullName ?? string.Empty;
            if (typeName == "Jint.Runtime.PromiseRejectedException" && current.InnerException is not null)
            {
                current = current.InnerException;
                continue;
            }
            if (current.InnerException is not null
                && (typeName.EndsWith("RuntimeException", StringComparison.Ordinal)
                    || typeName.EndsWith("WrappedException", StringComparison.Ordinal)))
            {
                current = current.InnerException;
                continue;
            }
            break;
        }
        return current ?? ex;
    }
}
