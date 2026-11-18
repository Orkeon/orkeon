using System.Diagnostics;

namespace Orkeon.Application.Telemetry;

/// <summary>
/// The <see cref="ActivitySource"/>s the execution path emits spans on, one per concern.
/// <para>
/// They live in the Application layer because that is where the calls happen: the agent
/// loop, the tool dispatcher and the orchestrator are the code that knows which model
/// answered, which tool ran and for which agent. Until 2026-09-11 the sources were
/// declared in Infrastructure next to helpers nothing in production called; a run
/// produced no span at all. The names are unchanged, so an exporter registered with
/// <c>AddSource("Orkeon.Llm")</c> keeps receiving what it always should have.
/// </para>
/// <para>
/// Span names and attributes follow the OpenTelemetry generative-AI conventions
/// (<see cref="Orkeon.Constants.Llm.GenAiAttributes"/>): <c>chat {model}</c> on
/// <see cref="Llm"/>, <c>invoke_agent {agent}</c> on <see cref="Agent"/>,
/// <c>execute_tool {tool}</c> on <see cref="Tool"/>.
/// </para>
/// </summary>
public static class OrkeonActivitySources
{
    /// <summary>
    /// A fixed value kept for the callers that compiled it in. The sources and the meter report the version of the
    /// running release, not this constant (#418).
    /// </summary>
    public const string Version = "1.0.0";

    /// <summary>The version every source and the meter report: the running release's.</summary>
    internal static string ReleaseVersion => Orkeon.Domain.Constants.Platform.OrkeonRelease.Version;

    /// <summary>Crew-level operations (kickoff, batch, streaming).</summary>
    public static readonly ActivitySource Crew = new("Orkeon.Crew", ReleaseVersion);

    /// <summary>Agent turns: one <c>invoke_agent</c> span per task an agent works on.</summary>
    public static readonly ActivitySource Agent = new("Orkeon.Agent", ReleaseVersion);

    /// <summary>Task-level operations (planning, validation).</summary>
    public static readonly ActivitySource Task = new("Orkeon.Task", ReleaseVersion);

    /// <summary>Model calls: one <c>chat</c> span per request to a provider.</summary>
    public static readonly ActivitySource Llm = new("Orkeon.Llm", ReleaseVersion);

    /// <summary>Tool executions: one <c>execute_tool</c> span per call the model made.</summary>
    public static readonly ActivitySource Tool = new("Orkeon.Tool", ReleaseVersion);

    /// <summary>Memory operations (store, search, delete).</summary>
    public static readonly ActivitySource Memory = new("Orkeon.Memory", ReleaseVersion);

    /// <summary>EventHub messaging (publish, post, send, receive).</summary>
    public static readonly ActivitySource EventHub = new("Orkeon.EventHub", ReleaseVersion);

    /// <summary>The source of a procedural script's spans, owned by <c>Orkeon.Scripting</c>.</summary>
    private const string ScriptingName = "Orkeon.Scripting";

    /// <summary>Every source name, for <c>TracerProviderBuilder.AddSource</c>.</summary>
    public static readonly string[] AllNames =
    [
        Crew.Name, Agent.Name, Task.Name, Llm.Name, Tool.Name, Memory.Name, EventHub.Name, ScriptingName,
    ];

    /// <summary>
    /// The provider name in the convention's vocabulary: lower-case, so <c>OpenAI</c> and
    /// <c>openai</c> are the same provider to a backend.
    /// </summary>
    public static string ProviderName(string? providerName) =>
#pragma warning disable CA1308 // the convention's vocabulary is lower-case; this is a normalisation to it, not a comparison
        string.IsNullOrWhiteSpace(providerName) ? "unknown" : providerName.Trim().ToLowerInvariant();
#pragma warning restore CA1308
}
