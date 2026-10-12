using System.Collections.Concurrent;

namespace Orkeon.Application.Crew.Execution;

/// <summary>The run in progress remembers which LLM settings fell back to a buffered call.</summary>
/// <remarks>
/// Backed by <see cref="AsyncLocal{T}"/>, like <see cref="CrewStreamScope"/>: the set follows the run's
/// flow into the tasks it starts, and calls run concurrently, so the set is thread-safe.
/// </remarks>
public static class LlmCallModeScope
{
    private static readonly AsyncLocal<ConcurrentDictionary<string, bool>?> Ambient = new();

    /// <summary>Opens the scope of a run; inside an open scope it reuses the enclosing set (a nested run is the same run); disposing the outermost restores none.</summary>
    public static IDisposable Open()
    {
        var enclosing = Ambient.Value;
        if (enclosing is not null)
            return new Scope(enclosing);

        Ambient.Value = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        return new Scope(enclosing);
    }

    /// <summary>Whether a run scope is open on the current flow.</summary>
    public static bool IsOpen => Ambient.Value is not null;

    /// <summary>Whether the setting already fell back during the run in progress.</summary>
    public static bool HasFallenBack(string settingKey) =>
        Ambient.Value?.ContainsKey(settingKey) ?? false;

    /// <summary>Records that the setting fell back during the run in progress.</summary>
    public static void RecordFallback(string settingKey) =>
        Ambient.Value?.TryAdd(settingKey, true);

    private sealed class Scope(ConcurrentDictionary<string, bool>? enclosing) : IDisposable
    {
        public void Dispose() => Ambient.Value = enclosing;
    }
}
