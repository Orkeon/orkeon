using Xunit;

namespace Orkeon.Tests.Shared.Assertions;

/// <summary>
/// xUnit assertion helpers replacing FluentAssertions usage in the test suite.
/// Pulled into tests via <c>using static Orkeon.Tests.Shared.Assertions.AssertEx;</c>.
/// </summary>
public static class AssertEx
{
    /// <summary>
    /// Asserts that <paramref name="action"/> throws <typeparamref name="TException"/>
    /// and that the exception message contains <paramref name="messageContains"/>.
    /// Returns the thrown exception so callers can run further checks.
    /// </summary>
    public static TException ThrowsContaining<TException>(Action action, string messageContains)
        where TException : Exception
    {
        var ex = Assert.Throws<TException>(action);
        Assert.Contains(messageContains, ex.Message);
        return ex;
    }

    /// <summary>Async variant of <see cref="ThrowsContaining{TException}(Action, string)"/>.</summary>
    public static async Task<TException> ThrowsContainingAsync<TException>(Func<Task> action, string messageContains)
        where TException : Exception
    {
        var ex = await Assert.ThrowsAsync<TException>(action);
        Assert.Contains(messageContains, ex.Message);
        return ex;
    }

    /// <summary>
    /// Asserts that <paramref name="action"/> throws <typeparamref name="TException"/>
    /// and that <paramref name="predicate"/> holds on the thrown instance.
    /// </summary>
    public static TException ThrowsWhere<TException>(Action action, Func<TException, bool> predicate)
        where TException : Exception
    {
        var ex = Assert.Throws<TException>(action);
        Assert.True(predicate(ex), $"Predicate failed for {typeof(TException).Name}: {ex.Message}");
        return ex;
    }

    /// <summary>
    /// Asserts that <paramref name="action"/> throws an exception (any type) whose
    /// message — or that of any inner exception — contains <paramref name="fragment"/>.
    /// Used for cases where the runtime wraps our CLR exception in a JS promise
    /// rejection (e.g. <c>PromiseRejectedException</c>).
    /// </summary>
    public static async Task<Exception> ThrowsContainingAcrossChainAsync(Func<Task> action, string fragment)
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(action);
        Assert.Contains(fragment, ChainText(ex));
        return ex;
    }

    /// <summary>
    /// Asserts that <paramref name="action"/> does not throw. Wraps
    /// <see cref="Record.Exception(Action)"/> for convenience.
    /// </summary>
    public static void DoesNotThrow(Action action)
    {
        var ex = Record.Exception(action);
        Assert.Null(ex);
    }

    /// <summary>Asserts that <paramref name="actual"/> matches <paramref name="pattern"/>.</summary>
    public static void MatchesRegex(string pattern, string actual)
    {
        Assert.Matches(pattern, actual);
    }

    /// <summary>
    /// Asserts that exactly one element of <paramref name="source"/> satisfies
    /// <paramref name="predicate"/>; returns it for further inspection.
    /// </summary>
    public static T Single<T>(IEnumerable<T> source, Func<T, bool> predicate)
    {
        var match = source.Where(predicate).ToList();
        Assert.Single(match);
        return match[0];
    }

    /// <summary>
    /// Asserts that <paramref name="source"/> contains at least one element satisfying
    /// <paramref name="predicate"/>; returns the first match.
    /// </summary>
    public static T ContainsMatching<T>(IEnumerable<T> source, Func<T, bool> predicate)
    {
        foreach (var item in source)
            if (predicate(item)) return item;
        Assert.Fail("No element matched the predicate.");
        return default!;
    }

    /// <summary>
    /// Concatenates the message of <paramref name="ex"/> and every inner exception so
    /// callers can substring-match across the whole chain.
    /// </summary>
    public static string ChainText(Exception ex)
    {
        var parts = new List<string>();
        for (var e = (Exception?)ex; e is not null; e = e.InnerException)
            parts.Add($"{e.GetType().Name}: {e.Message}");
        if (ex is AggregateException agg)
            foreach (var inner in agg.Flatten().InnerExceptions)
                parts.Add($"{inner.GetType().Name}: {inner.Message}");
        return string.Join(" | ", parts);
    }
}
