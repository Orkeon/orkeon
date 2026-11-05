namespace Orkeon.Cli.Abstractions.Console;

/// <summary>A tool call that begins.</summary>
/// <param name="Id">Tells this call from those running beside it.</param>
/// <param name="Tool">The tool's name.</param>
/// <param name="Thread">The screen the call belongs to; null is the main one.</param>
public sealed record ToolCallStarted(long Id, string Tool, string? Thread)
{
    /// <summary>What the call works on, for display only: a path, a command, a query; empty when it shows none.</summary>
    public string Subject { get; init; } = string.Empty;
}

/// <summary>A tool call that ended.</summary>
/// <param name="Id">The <see cref="ToolCallStarted.Id"/> of the call.</param>
/// <param name="Thread">The screen the call belongs to; null is the main one.</param>
/// <param name="Succeeded">False when the tool failed, threw or was refused.</param>
public sealed record ToolCallFinished(long Id, string? Thread, bool Succeeded);

/// <summary>Hears the tool calls of the session, on the thread that makes them.</summary>
public interface IToolActivityObserver
{
    /// <summary>A call begins.</summary>
    void Started(ToolCallStarted toolCall);

    /// <summary>A call ended, whatever became of it.</summary>
    void Finished(ToolCallFinished toolCall);
}

/// <summary>Where the invocation point meets the console that shows the calls; no observer means no cost.</summary>
public sealed class ToolActivityFeed
{
    /// <summary>The observer; null while no console shows the calls.</summary>
    public IToolActivityObserver? Observer { get; set; }
}
