using Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;

namespace Orkeon.Studio.Wpf.ViewModels.Capture;

/// <summary>
/// Waiting on a condition rather than on a clock.
/// <para>
/// An arrange that slept would be a guess twice over: too short on a loaded machine, and pure
/// waste on a fast one, several hundred times over. The timeout is the only wall clock, and
/// reaching it is a failure with the condition's own source text in the message.
/// </para>
/// </summary>
internal static class CaptureWait
{
    /// <summary>How long a condition has to become true before the stop is called failed.</summary>
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    /// <summary>Yields until <paramref name="condition"/> holds, or throws saying which one did not.</summary>
    public static async Task UntilAsync(
        Func<bool> condition,
        [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(condition))] string? description = null)
    {
        ArgumentNullException.ThrowIfNull(condition);

        var deadline = DateTimeOffset.UtcNow + Limit;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow > deadline)
                throw new TimeoutException($"the campaign waited for {description} and it never became true");

            await Task.Yield();
        }
    }
}

/// <summary>Ready-made arrange bodies, so a stop that only navigates says so in one word.</summary>
internal static class CaptureAction
{
    /// <summary>Arranges nothing.</summary>
    public static Func<CaptureContext, Task> None { get; } = static _ => Task.CompletedTask;

    /// <summary>Wraps a synchronous body.</summary>
    public static Func<CaptureContext, Task> Sync(Action<CaptureContext> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        return context =>
        {
            body(context);
            return Task.CompletedTask;
        };
    }
}

/// <summary>
/// One shot of the campaign, declared as data: where it stands, what it arranges, why that state
/// is worth a pixel, and which ViewModel gates it claims to light up.
/// <para>
/// <see cref="Covers"/> is the part that matters most. It turns the catalogue from documentation
/// into an assertion: the campaign is replayed headless on Linux and every claim is checked, so a
/// stop that quietly stops reaching its state fails the build instead of writing a confident
/// picture of the wrong screen on a machine nobody is watching.
/// </para>
/// </summary>
internal sealed record CaptureStop
{
    /// <summary>The file-name slug; unique across the catalogue.</summary>
    public required string Name { get; init; }

    /// <summary>The grouping this shot belongs to.</summary>
    public required CaptureCategory Category { get; init; }

    /// <summary>Navigated to before <see cref="Arrange"/> runs.</summary>
    public required CaptureScreen Screen { get; init; }

    /// <summary>Why this state is worth a pixel. Never empty.</summary>
    public required string Because { get; init; }

    /// <summary>Dotted paths from the shell, asserted TRUE after the arrange.</summary>
    public IReadOnlyList<string> Covers { get; init; } = [];

    /// <summary>Dotted paths the stop deliberately drives to FALSE — the empty-state shots.</summary>
    public IReadOnlyList<string> CoversFalse { get; init; } = [];

    /// <summary>Which mode passes this stop belongs to.</summary>
    public CaptureModes Modes { get; init; } = CaptureModes.Both;

    /// <summary>Which of the two seeded machines it stands on.</summary>
    public CaptureWorldKind World { get; init; } = CaptureWorldKind.Seeded;

    /// <summary>Whether the language sweep repeats this stop in every supported language.</summary>
    public bool SweepsLanguages { get; init; }

    /// <summary>
    /// Whether two consecutive identical images are legitimate here. Off by default, because a
    /// shot identical to the one before it almost always means the stop changed nothing.
    /// </summary>
    public bool AllowSameAsPrevious { get; init; }

    /// <summary>Puts the ViewModels into the state the stop names.</summary>
    public Func<CaptureContext, Task> Arrange { get; init; } = CaptureAction.None;

    /// <summary>Puts them back, so the next stop starts from a known place.</summary>
    public Func<CaptureContext, Task> Teardown { get; init; } = CaptureAction.None;
}
