using System.Diagnostics;
using Orkeon.Constants.Llm;

namespace Orkeon.Scripting.Telemetry;

/// <summary>
/// <see cref="ActivitySource"/> shared by the Orkeon scripting runtime. Spans emitted
/// here cover crew runs, agent body invocations, LLM calls and tool calls. The source
/// is registered unconditionally; spans only materialise when at least one
/// <see cref="ActivityListener"/> is attached (default OpenTelemetry behaviour).
/// </summary>
public static class ScriptingActivitySource
{
    /// <summary>ActivitySource name used by listeners.</summary>
    public const string Name = "Orkeon.Scripting";

    /// <summary>Shared source instance used across the runtime.</summary>
    public static readonly ActivitySource Instance = new(Name);

    /// <summary>Span name for a crew run (root span).</summary>
    public const string CrewRunSpan = "crew.run";

    /// <summary>Span name for a single agent body invocation.</summary>
    public const string AgentRunSpan = GenAiAttributes.OperationInvokeAgent;

    /// <summary>Span name for an LLM call (complete/chat/extract/decide/embed/act).</summary>
    public const string LlmCallSpan = GenAiAttributes.OperationChat;

    /// <summary>Span name for a tool call.</summary>
    public const string ToolCallSpan = GenAiAttributes.OperationExecuteTool;

    /// <summary>
    /// Starts the span of one tool call, its tool tags set from the start: a listener's
    /// <c>ActivityStarted</c> runs inside <c>StartActivity</c>, before any later <c>SetTag</c>.
    /// </summary>
    internal static Activity? StartToolCall(string toolName) =>
        Instance.StartActivity(
            GenAiAttributes.SpanName(ToolCallSpan, toolName),
            ActivityKind.Internal,
            parentContext: default,
            tags:
            [
                new(GenAiAttributes.OperationName, GenAiAttributes.OperationExecuteTool),
                new(GenAiAttributes.ToolName, toolName),
            ]);
}
