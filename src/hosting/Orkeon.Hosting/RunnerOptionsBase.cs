using CommandLine;
using Orkeon.Compliance.Vfs;

namespace Orkeon.Hosting;

/// <summary>
/// Base CLI options shared by all Orkeon runners.
/// Concrete runners inherit from this class and add domain-specific options.
/// Ensures a consistent CLI experience across all runners.
/// </summary>
[SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: normalizes the user-supplied --llm-log-path argument before the VFS mounts that will host it are provisioned.")]
public abstract class RunnerOptionsBase
{
    /// <summary>
    /// Path to the crew definition (.yaml or .ork.ts). Required for every mode except
    /// <c>--list-tools</c>, which dumps the runtime tool registry without loading a crew.
    /// </summary>
    [Option('c', "config", Required = false,
        HelpText = "Path to the crew definition. Accepts .yaml (YAML loader) or .ork.ts " +
                   "(Orkéon Scripting DSL, loaded via Jint + esbuild). " +
                   "Required unless --list-tools is used.")]
    public string ConfigPath { get; set; } = "";

    /// <summary>Path to appsettings.json (defaults to same dir as config).</summary>
    [Option('s', "settings", Required = false,
        HelpText = "Path to appsettings.json (defaults to same dir as config).")]
    public string? SettingsPath { get; set; }

    /// <summary>Repeatable mount strings in Docker-style format.</summary>
    [Option('m', "mount", Required = false,
        HelpText = "File system mount(s) in Docker-style format: <physical>:<virtual>:<rights>[;sub:rights]. Repeatable.")]
    public IEnumerable<string> Mounts { get; set; } = [];

    /// <summary>Allow mounts whose base path is outside the cwd.</summary>
    [Option("allow-external-mounts", Required = false, Default = false,
        HelpText = "Allow mounts from directories outside the workspace root. Mount base paths are added to the security whitelist.")]
    public bool AllowExternalMounts { get; set; }

    /// <summary>Verbosity level 0-2.</summary>
    [Option('v', "verbose", Required = false, Default = 0,
        HelpText = "Verbosity level: 0=quiet, 1=LLM & tool exchanges, 2=full debug.")]
    public int Verbose { get; set; }

    /// <summary>Enable LLM exchange logging to JSONL files.</summary>
    [Option("llm-log", Required = false, Default = false,
        HelpText = "Enable LLM exchange logging to files (request/response headers + payload). " +
                   "Logs are written to './llm-logs' or the path specified by --llm-log-path.")]
    public bool LlmLogEnabled { get; set; }

    /// <summary>Custom directory for LLM exchange logs (implies --llm-log).</summary>
    [Option("llm-log-path", Required = false, Default = null,
        HelpText = "Directory for LLM exchange log files (.jsonl). Implies --llm-log. " +
                   "Defaults to './llm-logs' when --llm-log is used without --llm-log-path.")]
    public string? LlmLogPath { get; set; }

    /// <summary>Repeatable KEY=VALUE variables forwarded to CrewInput.</summary>
    [Option('V', "var", Required = false,
        HelpText = "Variable for CrewInput (KEY=VALUE format). Repeatable. " +
                   "Used by task description templates: {KEY} → VALUE.")]
    public IEnumerable<string> Variables { get; set; } = [];

    /// <summary>Initial context string passed to CrewInput.</summary>
    [Option("initial-context", Required = false, Default = null,
        HelpText = "Initial context string passed to CrewInput.")]
    public string? InitialContext { get; set; }

    /// <summary>
    /// Dry-run: resolve settings, build the host and load the crew (strict tool
    /// resolution), without probing the LLM endpoint or running any kickoff.
    /// </summary>
    [Option("validate", Required = false, Default = false,
        HelpText = "Dry-run: resolve settings, build the host and load the crew (strict " +
                   "tool resolution) WITHOUT probing the LLM endpoint or running a kickoff. " +
                   "Prints 'VALIDATION OK: <config> (agents=N, tasks=M, tools resolved=K)' " +
                   "and exits 0, or reports the load error and exits non-zero.")]
    public bool Validate { get; set; }

    /// <summary>
    /// Build the host and print the sorted registry tool names (one per line) to stdout,
    /// then exit. The runtime tool manifest consumed by tooling/linting.
    /// </summary>
    [Option("list-tools", Required = false, Default = false,
        HelpText = "Build the host and print the sorted list of registered tool names " +
                   "(one per line) to stdout, then exit 0. Logs stay on stderr; no crew " +
                   "is loaded, so --config is not required.")]
    public bool ListTools { get; set; }

    /// <summary>
    /// Resolves the effective LLM log directory path.
    /// Returns <c>null</c> if logging is disabled; otherwise the resolved absolute path.
    /// <c>--llm-log-path</c> implies <c>--llm-log</c>.
    /// </summary>
    public string? ResolvedLlmLogPath
    {
        get
        {
            if (!LlmLogEnabled && string.IsNullOrWhiteSpace(LlmLogPath))
                return null;

            // --llm-log-path implies --llm-log
            var dir = string.IsNullOrWhiteSpace(LlmLogPath) ? "llm-logs" : LlmLogPath;
            return Path.GetFullPath(dir);
        }
    }

    /// <summary>
    /// Parses <c>--var KEY=VALUE</c> entries into a dictionary. Returns an empty dict
    /// if no <c>--var</c> was supplied. Throws <see cref="FormatException"/> if any
    /// entry is malformed (no <c>=</c>, empty key, or whitespace-only key).
    /// </summary>
    /// <remarks>
    /// This is a method (not a property) because it parses external input and can throw
    /// <see cref="FormatException"/>; a property getter must not raise exceptions (CA1065).
    /// </remarks>
    public IReadOnlyDictionary<string, string> ParseVariables()
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in Variables)
        {
            var idx = raw.IndexOf('=', StringComparison.Ordinal);
            if (idx <= 0)
                throw new FormatException(
                    $"Invalid --var '{raw}'. Expected KEY=VALUE format with non-empty KEY.");
            var key = raw[..idx].Trim();
            var val = raw[(idx + 1)..];
            if (string.IsNullOrEmpty(key))
                throw new FormatException($"Invalid --var '{raw}'. KEY must not be empty.");
            dict[key] = val;
        }
        return dict;
    }
}
