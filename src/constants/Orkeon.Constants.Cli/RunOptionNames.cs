namespace Orkeon.Constants.Cli;

/// <summary>
/// The option names Orkeon's runners accept, as CommandLineParser's <c>[Option]</c> wants them:
/// bare, without the leading dashes.
/// <para>
/// The same grammar is declared in three places - the YAML runner's options, the scripting
/// runner's options, and Orkeon Studio's prediction of the command line it is about to spawn -
/// in projects that cannot reference each other. Studio builds an argument list for a process it
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

    /// <summary>Allow mounts whose base path sits outside the workspace root.</summary>
    public const string AllowExternalMounts = "allow-external-mounts";

    /// <summary>Verbosity level.</summary>
    public const string Verbose = "verbose";

    /// <summary>Enable LLM exchange logging.</summary>
    public const string LlmLog = "llm-log";

    /// <summary>Where the exchange log files land.</summary>
    public const string LlmLogPath = "llm-log-path";

    /// <summary>A template variable, <c>name=value</c>.</summary>
    public const string Var = "var";

    /// <summary>Text prepended to the crew's first task.</summary>
    public const string InitialContext = "initial-context";

    /// <summary>Validate the definition and exit without running it.</summary>
    public const string Validate = "validate";

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
