namespace Orkeon.Constants.Configuration;

/// <summary>
/// The environment variables Orkeon reads by their name — each name written once, here, and the
/// table that says who reads it, what value it takes and what it does.
/// <para>
/// Two other kinds of variables are not names: the ones that carry a setting
/// (<see cref="SettingsPrefix"/> + the setting's path, <c>:</c> written <c>__</c>) and the ones a
/// setting names (<c>Llm:ApiKeyEnvVar</c>). The reference page — <c>docs/reference/configuration.md</c>,
/// "Environment variables" — and <c>orkeon settings env</c> list the three kinds; a test holds the
/// page to <see cref="ReadByName"/>.
/// </para>
/// <para>
/// A variable a component starts to read by its name is added here, with its row: the constant
/// alone fails that test.
/// </para>
/// </summary>
public static class EnvironmentVariableNames
{
    /// <summary>
    /// The prefix of the variables every Orkeon host reads as configuration, prefix removed:
    /// <c>ORKEON_Llm__Model</c> is <c>Llm:Model</c>. The first stop of the secret chain reads the
    /// same prefix before a secret's name (<c>ORKEON_TAVILY_API_KEY</c>).
    /// </summary>
    public const string SettingsPrefix = "ORKEON_";

    /// <summary>
    /// The variable <c>orkeon init --api-key-env</c> and Orkeon Studio propose for the key of the
    /// default model: the one the configuration reads natively as <c>Llm:ApiKey</c>.
    /// </summary>
    public const string LlmApiKeySetting = "ORKEON_Llm__ApiKey";

    /// <summary>Stands for <c>--allow-external-mounts</c> on every run.</summary>
    public const string AllowExternalMounts = "ORKEON_ALLOW_EXTERNAL_MOUNTS";

    /// <summary>An unexpected error prints its whole exception instead of one line.</summary>
    public const string Debug = "ORKEON_DEBUG";

    /// <summary>Marks the processes <c>orkeon mcp serve</c> starts, so that it never starts itself.</summary>
    public const string McpServe = "ORKEON_MCP_SERVE";

    /// <summary>Where the scripting toolchain finds esbuild.</summary>
    public const string EsbuildPath = "ORKEON_ESBUILD_PATH";

    /// <summary>The directory Orkeon Studio looks for the <c>orkeon</c> executable in.</summary>
    public const string CliDirectory = "ORKEON_CLI_DIR";

    /// <summary>The folder Orkeon Studio keeps its teams in.</summary>
    public const string StudioTeamsRoot = "ORKEON_STUDIO_TEAMS_ROOT";

    /// <summary>The variable <c>orkeon llm</c> reads a key from when <c>--api-key-env</c> names no other.</summary>
    public const string LlmCommandApiKey = "ORKEON_LLM_API_KEY";

    /// <summary>The variable Orkeon Studio keeps the key of an OpenAI-compatible model setting in.</summary>
    public const string CustomLlmApiKey = "ORKEON_CUSTOM_LLM_API_KEY";

    /// <summary>The bot token of the Discord channel, unless the settings name another variable.</summary>
    public const string DiscordToken = "ORKEON_DISCORD_TOKEN";

    /// <summary>The key that registers the Brave search tool; also a configuration key of the same name.</summary>
    public const string BraveApiKey = "BRAVE_API_KEY";

    /// <summary>The OpenTelemetry endpoint: set, it turns the export on.</summary>
    public const string OtlpEndpoint = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>The address of Ollama when the provider is given none.</summary>
    public const string OllamaBaseUrl = "OLLAMA_BASE_URL";

    /// <summary>The Terminal.Gui driver of the text interfaces.</summary>
    public const string TuiDriver = "TUI_DRIVER";

    /// <summary>The start-up trace of the split-pane console.</summary>
    public const string TuiDiagnostics = "TUI_DIAG";

    /// <summary>The root of the per-user settings under Linux and macOS.</summary>
    public const string XdgConfigHome = "XDG_CONFIG_HOME";

    /// <summary>Set to <c>true</c> by a continuous-integration runner: the text interfaces do not open.</summary>
    public const string ContinuousIntegration = "CI";

    /// <summary>The terminal type: unset under Linux, the text interfaces do not open.</summary>
    public const string Terminal = "TERM";

    private const string Cli = "orkeon";
    private const string ServiceHost = "orkeon-host";
    private const string Repl = "orkeon-repl";
    private const string Studio = "orkeon-studio";
    private const string StudioConfig = "orkeon-studio-config";
    private const string StudioRun = "orkeon-studio-run";

    /// <summary>
    /// Every variable a binary reads by its name, in the order the reference lists them: Orkeon's
    /// own, then the ones it shares with other tools.
    /// </summary>
    public static IReadOnlyList<EnvironmentVariableEntry> ReadByName { get; } =
    [
        new(AllowExternalMounts, [Cli], "`1`, `true` or `yes`",
            "Stands for `--allow-external-mounts` on every `orkeon run` and `orkeon rag`: a `--mount` may point outside " +
            "the working directory. A sandboxed deployment sets it once, its own boundary being the isolation."),
        new(Debug, [Cli], "`1`, `true` or `yes`",
            "An unexpected error prints its exception — the chain of types and the stack — instead of one line."),
        new(McpServe, [Cli], "set by `orkeon mcp serve`, never by hand",
            "Marks every process `orkeon mcp serve` starts. Under it `orkeon mcp serve` refuses to start, so settings " +
            "that list it among their own MCP servers cannot start it in a loop."),
        new(EsbuildPath, [Cli, ServiceHost, Repl], "the path of an esbuild executable",
            "Where a `.ork.ts` script finds esbuild, after `Orkeon:Scripting:Toolchain:EsbuildPath` and before the copy " +
            "shipped beside the binary. The launchers of an installation set it to the esbuild they ship when it is unset."),
        new(LlmCommandApiKey, [Cli], "an API key",
            "The variable `orkeon llm probe` and `orkeon llm models` read the key from when `--api-key-env` names no other."),
        new(BraveApiKey, [Cli, ServiceHost], "a Brave Search API key",
            "Registers the `brave_search` tool. It is read as a configuration key first, so the settings file and " +
            "`ORKEON_BRAVE_API_KEY` give it too."),
        new(DiscordToken, [ServiceHost], "a Discord bot token",
            "The token of the Discord channel: the default of `Orkeon:Host:Discord:TokenEnvironmentVariable`, which may " +
            "name another variable."),
        new(OllamaBaseUrl, [Cli, ServiceHost, Repl], "the address of an Ollama server",
            "The address the Ollama provider uses when it is given none: `Llm:BaseUrl` wins over it, and " +
            "`http://localhost:11434` is what remains without either."),
        new(CliDirectory, [Studio, StudioConfig, StudioRun], "a directory",
            "Where Orkeon Studio looks for the `orkeon` executable, after `--cli-dir` and its own install directory, " +
            "before `PATH`."),
        new(StudioTeamsRoot, [Studio, StudioRun], "a fully qualified directory",
            "The folder Orkeon Studio keeps its teams in. It wins over `--teams-root` and over the folder chosen in " +
            "Settings › Studio."),
        new(CustomLlmApiKey, [Studio, StudioConfig], "an API key",
            "The variable Orkeon Studio proposes for the key of an OpenAI-compatible model setting, and writes in that " +
            "setting's `ApiKeyEnvVar`: a run reads it through that key, not by this name."),
        new(TuiDriver, [Repl, StudioConfig, StudioRun], "`windows`, `dotnet` or `ansi`",
            "The Terminal.Gui driver of the text interfaces, for diagnosis. Unset: `windows` under Windows, `dotnet` " +
            "elsewhere — `ansi` draws nothing under WSL and in many container terminals."),
        new(TuiDiagnostics, [Repl], "`1`",
            "The split-pane console writes `[tui-diag]` lines on stderr while it starts: the terminal, the driver chosen."),
        new(OtlpEndpoint, [Cli, ServiceHost], "the address of an OTLP collector",
            "Turns the OpenTelemetry export on when `Telemetry:OtlpEndpoint` is empty; the exporter reads the address, " +
            "and the other `OTEL_EXPORTER_OTLP_*` variables, by itself."),
        new(XdgConfigHome, [Cli, ServiceHost, Repl, StudioConfig, StudioRun], "an absolute directory",
            "Under Linux and macOS, the root of the per-user settings — `$XDG_CONFIG_HOME/Orkeon/appsettings.json`, " +
            "`~/.config` when it is unset — and of the systemd user units `orkeon forge schedule` installs."),
        new(ContinuousIntegration, [Repl, StudioConfig, StudioRun], "`true`",
            "No text interface opens: `orkeon-repl` falls back to its plain console, the two Studio terminal " +
            "applications say they need a terminal and exit."),
        new(Terminal, [Repl, StudioConfig, StudioRun], "a terminal type",
            "Under Linux, unset or empty has the effect of `CI=true`: there is no terminal to draw in."),
    ];

    /// <summary>
    /// The ending of a settings key that holds the name of a variable rather than a value:
    /// <c>Llm:ApiKeyEnvVar</c>, <c>Orkeon:Host:Discord:TokenEnvironmentVariable</c>.
    /// </summary>
    public static IReadOnlyList<string> NamingKeySuffixes { get; } = ["EnvVar", "EnvironmentVariable"];
}

/// <summary>One environment variable a binary reads by its name.</summary>
public sealed class EnvironmentVariableEntry
{
    /// <summary>Creates the row of <paramref name="name"/>.</summary>
    /// <param name="name">The variable, as the environment spells it.</param>
    /// <param name="readBy">The shipped binaries that read it.</param>
    /// <param name="value">The value it expects, in a few words.</param>
    /// <param name="effect">What it does, in a sentence or two.</param>
    public EnvironmentVariableEntry(string name, IReadOnlyList<string> readBy, string value, string effect)
    {
        Name = name;
        ReadBy = readBy;
        Value = value;
        Effect = effect;
    }

    /// <summary>The variable, as the environment spells it.</summary>
    public string Name { get; }

    /// <summary>The shipped binaries that read it.</summary>
    public IReadOnlyList<string> ReadBy { get; }

    /// <summary>The value it expects, in a few words.</summary>
    public string Value { get; }

    /// <summary>What it does, in a sentence or two.</summary>
    public string Effect { get; }
}
