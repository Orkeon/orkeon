namespace Orkeon.Constants.Configuration;

/// <summary>
/// The configuration paths more than one project has to spell identically.
/// <para>
/// Only those. Orkeon declares 56 distinct <c>Orkeon:*</c> keys and 51 of them are written in a
/// single project, where they belong — a key one component reads is that component's business.
/// What lands here is the handful the engine and the tooling must agree on and cannot share by
/// reference, because <c>Orkeon.Studio.Core</c> may not reference <c>Orkeon.Hosting</c> or
/// <c>Orkeon.Infrastructure</c> (ADR-009).
/// </para>
/// <para>
/// A misspelt configuration path does not fail: it reads as absent, the caller falls back to a
/// default, and the operator's setting is ignored in silence. That is the failure mode these
/// constants exist to remove.
/// </para>
/// </summary>
public static class ConfigurationKeys
{
    /// <summary>
    /// The agent-facing mount array. The runner appends its own entries after whatever this
    /// declares, and Orkeon Studio edits the same array — which is why both must name it the
    /// same way, and why Studio predicts the index each <c>--mount</c> will occupy.
    /// </summary>
    public const string FileSystemMounts = "Orkeon:FileSystem:Mounts";

    /// <summary>
    /// Infrastructure mounts: resolvable by the VFS, never listed to an agent (ADR-008). Its own
    /// key rather than a flag on <see cref="FileSystemMounts"/>, because the mount-string grammar
    /// has no room for visibility.
    /// </summary>
    public const string FileSystemInternalMounts = "Orkeon:FileSystem:InternalMounts";

    /// <summary>
    /// Root of the RAG subsystem's options. Bound by <c>AddOrkeonRag</c>, and read by Studio to
    /// show what a machine has configured.
    /// </summary>
    public const string Rag = "Orkeon:Rag";

    /// <summary>
    /// The context window a session budgets against. Read by the token-budget tool and by the
    /// console's fidelity wiring, which live in projects that do not share a layer.
    /// </summary>
    public const string CliSessionContextWindowTokens = "Orkeon:Cli:Session:ContextWindowTokens";

    /// <summary>
    /// The LLM section, at the configuration ROOT rather than under <c>Orkeon:</c> - the one
    /// section that is, for historical reasons the settings files already carry. Read by the
    /// REPL's bootstrapper, by the shared runner host and by the CLI's doctor and forge verbs,
    /// in three projects that cannot reference each other.
    /// </summary>
    public const string LlmSection = "Llm";

    /// <summary>
    /// The thinking sub-section of <see cref="LlmSection"/>. Read by two projects, and its
    /// absence is not an error anywhere - which is exactly why a spelling that drifted would be
    /// read as "thinking not configured" rather than reported.
    /// </summary>
    public const string ThinkingSection = "Thinking";
}
