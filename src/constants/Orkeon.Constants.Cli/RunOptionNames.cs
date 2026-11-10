namespace Orkeon.Constants.Cli;

/// <summary>
/// The option names Orkeon's runners accept, as CommandLineParser's <c>[Option]</c> wants them:
/// bare, without the leading dashes.
/// <para>
/// The same grammar is declared in three places - the YAML runner's options, the scripting
/// runner's options, and a client's prediction of the command line it is about to spawn -
/// in projects that cannot reference each other. A client builds an argument list for a process it
/// has no reference to, so a flag renamed on one side and not the other produces a launch that
/// dies on an unrecognised option, at the one moment the user has no console to read it in.
/// </para>
/// <para>
/// Bare names rather than <c>--</c>-prefixed ones because that is the form the attribute
/// requires, and an attribute argument must be a compile-time constant. <see cref="Flag"/>
/// composes the command-line spelling for the callers that need it.
/// </para>
/// </summary>
public static class RunOptionNames
{
    /// <summary>Path to the settings file.</summary>
    public const string Settings = "settings";

    /// <summary>A VFS mount, <c>physical:virtual:rights</c>.</summary>
    public const string Mount = "mount";

    /// <summary>
    /// The id of a settings mount to use for the run, when several entries declare one virtual
    /// root (the <c>&lt;ulid&gt;|</c> prefix of an entry, VFS-90). Several go space-separated
    /// after ONE flag, like <see cref="Mount"/>.
    /// </summary>
    public const string MountId = "mount-id";

    /// <summary>Allow mounts whose base path sits outside the workspace root.</summary>
    public const string AllowExternalMounts = "allow-external-mounts";

    /// <summary>
    /// The host LLM profile (<c>Llm:Profiles:&lt;id&gt;</c>) the run takes as its default
    /// — what a team's launchers write for the team's model setting.
    /// </summary>
    public const string LlmProfile = "llm-profile";

    /// <summary>Verbosity level.</summary>
    public const string Verbose = "verbose";

    /// <summary>Enable LLM exchange logging.</summary>
    public const string LlmLog = "llm-log";

    /// <summary>Where the exchange log files land.</summary>
    public const string LlmLogPath = "llm-log-path";

    /// <summary>Answer every LLM call from a JSONL responses file, offline.</summary>
    public const string LlmMock = "llm-mock";

    /// <summary>Record every LLM call of the run, with its answer, in a JSONL cassette.</summary>
    public const string LlmRecord = "llm-record";

    /// <summary>Answer every LLM call of the run from a cassette recorded by <c>--llm-record</c>, offline.</summary>
    public const string LlmReplay = "llm-replay";

    /// <summary>Bind the global <c>test</c> namespace (<c>test.mockLlm</c>, <c>test.mockTool</c>) in a script.</summary>
    public const string Test = "test";

    /// <summary>A template variable, <c>name=value</c>.</summary>
    public const string Var = "var";

    /// <summary>Text prepended to the crew's first task.</summary>
    public const string InitialContext = "initial-context";

    /// <summary>Validate the definition and exit without running it.</summary>
    public const string Validate = "validate";

    /// <summary>Resumes a checkpointed run: <c>--resume &lt;id&gt;|latest</c>.</summary>
    public const string Resume = "resume";

    /// <summary>Answers the question a resumed run stopped on: <c>--resume &lt;id&gt; --input "..."</c> (FWK-23).</summary>
    public const string Input = "input";

    /// <summary>List the tools the host would register, then exit.</summary>
    public const string ListTools = "list-tools";

    /// <summary>Inline JSON inputs.</summary>
    public const string Inputs = "inputs";

    /// <summary>Path to a JSON file holding the inputs.</summary>
    public const string InputsFile = "inputs-file";

    /// <summary>Where the run's event stream is written.</summary>
    public const string Events = "events";

    /// <summary>Emit token-by-token generation deltas.</summary>
    public const string Stream = "stream";

    /// <summary>Which client is driving the run.</summary>
    public const string Client = "client";

    /// <summary>Memory ceiling, in megabytes.</summary>
    public const string MemoryLimitMb = "memory-limit-mb";

    /// <summary>
    /// The command-line spelling of an option name: <c>--settings</c> for
    /// <see cref="Settings"/>. For the tooling that composes an argument list rather than
    /// declaring an attribute.
    /// </summary>
    /// <param name="name">A bare option name from this class.</param>
    /// <returns>The name prefixed with <c>--</c>.</returns>
    public static string Flag(string name) => "--" + name;
}
