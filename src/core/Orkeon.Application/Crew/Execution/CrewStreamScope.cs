using System.Threading.Channels;
using Orkeon.Application.Interfaces.Services;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// The live stream of the crew run in progress (GAP-32): where the run's events go while it runs,
/// when its caller asked for them (<see cref="ICrewOrchestrationService.KickoffStreamingAsync"/>). The
/// orchestrator opens a scope around every run — with the stream's writer, or with none for
/// <see cref="ICrewOrchestrationService.KickoffAsync"/> —, and three points of the run write into it
/// as they already notify: the execution hook's dispatcher (each task's start and end, the run's
/// failure), the tool-invocation point of the agent loops (each tool call) and the chat-client agent
/// loop (the model's text as it arrives). The execution hook the host registered is served as before.
/// </summary>
/// <remarks>
/// Backed by <see cref="AsyncLocal{T}"/>, like <c>LlmUsageScope</c> and <see cref="CrewPlanScope"/>:
/// the writer follows the run's flow across awaits and into the tasks it starts (<c>Task.Run</c>:
/// parallel waves, consensual candidates, <c>asyncExecution</c> tasks), and a scope opened inside an
/// async method never leaks to its caller. Every run opens its own: a crew kicked off from inside a
/// task — a tool that runs another crew — opens one without a writer, so it never writes into the
/// stream of the run that called it. The stream's channel is unbounded and written with
/// <c>TryWrite</c>: a slow reader never slows the run down.
/// </remarks>
public static class CrewStreamScope
{
    private static readonly AsyncLocal<ChannelWriter<CrewExecutionEvent>?> Ambient = new();

    /// <summary>Opens the scope of a run; disposing the handle restores the enclosing one.</summary>
    /// <param name="writer">
    /// Where the run's events go; null for a run nobody streams — which hides an enclosing run's
    /// stream from this one.
    /// </param>
    /// <returns>The handle that restores the enclosing scope.</returns>
    public static IDisposable Begin(ChannelWriter<CrewExecutionEvent>? writer)
    {
        var enclosing = Ambient.Value;
        Ambient.Value = writer;
        return new Scope(enclosing);
    }

    /// <summary>Whether the run in progress is streamed: what <see cref="Write"/> is handed goes somewhere.</summary>
    public static bool IsOpen => Ambient.Value is not null;

    /// <summary>
    /// Writes one event of the run in progress to its stream; nothing outside a streamed run. Never
    /// blocks and never throws: a stream its consumer already left takes nothing more.
    /// </summary>
    /// <param name="executionEvent">The event.</param>
    public static void Write(CrewExecutionEvent executionEvent)
    {
        ArgumentNullException.ThrowIfNull(executionEvent);
        Ambient.Value?.TryWrite(executionEvent);
    }

    private sealed class Scope(ChannelWriter<CrewExecutionEvent>? enclosing) : IDisposable
    {
        public void Dispose() => Ambient.Value = enclosing;
    }
}
