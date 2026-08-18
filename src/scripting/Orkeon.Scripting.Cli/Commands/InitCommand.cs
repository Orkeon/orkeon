using System.Text.Json;
using CommandLine;
using Orkeon.Hosting;
using Orkeon.Infrastructure.Constants.Llm;

namespace Orkeon.Scripting.Cli.Commands;

/// <summary>Options for <c>orkeon init</c>.</summary>
internal sealed class InitCommandOptions
{
    /// <summary>Provider preset; omitting it starts the interactive wizard.</summary>
    [Option('p', "provider", Required = false,
        HelpText = "Provider preset: ollama | docker-model-runner | openai | custom | none. Omit to run the interactive wizard.")]
    public string? Provider { get; set; }

    /// <summary>Base URL override (required for custom).</summary>
    [Option('u', "base-url", Required = false,
        HelpText = "LLM endpoint base URL. Required for --provider custom; presets have their own default.")]
    public string? BaseUrl { get; set; }

    /// <summary>Model identifier override (required for custom).</summary>
    [Option('m', "model", Required = false,
        HelpText = "Model identifier. Required for --provider custom; presets have their own default.")]
    public string? Model { get; set; }

    /// <summary>Environment variable that will hold the API key (the safe default).</summary>
    [Option('k', "api-key-env", Required = false,
        HelpText = "Environment variable that will hold the API key (default ORKEON_Llm__ApiKey, which the runtime reads natively). The key is never written to the file.")]
    public string? ApiKeyEnv { get; set; }

    /// <summary>API key stored inline in the file. Discouraged.</summary>
    [Option("api-key", Required = false,
        HelpText = "API key stored INLINE in the generated file — discouraged (plain text). Prefer --api-key-env.")]
    public string? ApiKey { get; set; }

    /// <summary>Target file; defaults to the global per-user config path.</summary>
    [Option("path", Required = false,
        HelpText = "Target file (default: the global per-user path, %APPDATA%\\Orkeon\\appsettings.json / ~/.config/Orkeon/appsettings.json).")]
    public string? OutputPath { get; set; }

    /// <summary>Allow overwriting an existing file.</summary>
    [Option('f', "force", Required = false, Default = false,
        HelpText = "Overwrite the target file if it already exists.")]
    public bool Force { get; set; }

    /// <summary>Skip the post-write connectivity probe.</summary>
    [Option("no-probe", Required = false, Default = false,
        HelpText = "Skip the connectivity probe after writing the configuration (CI/offline).")]
    public bool NoProbe { get; set; }

    /// <summary>Test seam: replaces <see cref="Console.In"/> for the interactive wizard.</summary>
    internal TextReader? InputOverride { get; set; }
}

/// <summary>
/// <c>orkeon init</c> — configuration assistant (WIN-02). Generates a valid
/// <c>appsettings.json</c> at the global per-user path (or <c>--path</c>) from either an
/// interactive wizard or non-interactive flags, then optionally probes the endpoint by
/// reusing the <c>llm models</c> catalogue plumbing (<see cref="LlmCatalogClient"/> — no
/// duplicated HTTP client).
/// </summary>
internal static class InitCommand
{
    private static readonly JsonSerializerOptions s_indentedJson = new() { WriteIndented = true };

    /// <summary>The env var the runtime configuration reads natively (AddEnvironmentVariables("ORKEON_")).</summary>
    private const string DefaultApiKeyEnv = "ORKEON_Llm__ApiKey";

    /// <summary>
    /// Docker Model Runner defaults. Shared with the committed appsettings template and with
    /// Orkeon Studio's preset catalogue, hence <see cref="DockerModelRunnerDefaults"/> rather
    /// than a private copy here.
    /// </summary>
    private const string DockerModelRunnerBaseUrl = DockerModelRunnerDefaults.BaseUrl;

    /// <inheritdoc cref="DockerModelRunnerDefaults.DefaultModel" />
    private const string DockerModelRunnerDefaultModel = DockerModelRunnerDefaults.DefaultModel;

    /// <inheritdoc cref="DockerModelRunnerDefaults.ApiKeyPlaceholder" />
    private const string DockerModelRunnerApiKeyPlaceholder = DockerModelRunnerDefaults.ApiKeyPlaceholder;

    /// <summary>Everything needed to write and probe one configuration.</summary>
    private sealed record InitPlan
    {
        public required string Provider { get; init; }          // ollama|docker-model-runner|openai|custom|none
        public string? BaseUrl { get; init; }
        public string? Model { get; init; }
        public string? InlineApiKey { get; init; }
        public string? ApiKeyEnvName { get; init; }
    }

    /// <summary>Dispatches an <c>orkeon init …</c> invocation (args already stripped of the verb).</summary>
    public static async Task<int> DispatchAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        using var parser = new Parser(s =>
        {
            s.HelpWriter = Console.Out;
            s.CaseInsensitiveEnumValues = true;
        });

        return await parser.ParseArguments<InitCommandOptions>(args)
            .MapResult(
                (InitCommandOptions o) => ExecuteAsync(o),
                _ => Task.FromResult(Program.ExitScriptError))
            .ConfigureAwait(false);
    }

    /// <summary>Runs the assistant described by <paramref name="options"/>; returns the CLI exit code.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Top-level CLI fault barrier, mirroring the run/rag verbs' exit-code contract.")]
    public static async Task<int> ExecuteAsync(InitCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            return await ExecuteCoreAsync(options, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Program.ExitCancelled;
        }
        catch (InvalidOperationException ex)
        {
            // E.g. no resolvable per-user configuration directory (bare container without
            // HOME): a configuration/input error with an actionable message, not a crash.
            await Console.Error.WriteLineAsync($"orkeon init: {ex.Message}").ConfigureAwait(false);
            return Program.ExitScriptError;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"orkeon init: unexpected error [{ex.GetType().FullName}]: {ex.Message}")
                .ConfigureAwait(false);
            return Program.ExitRuntimeError;
        }
    }

    private static async Task<int> ExecuteCoreAsync(InitCommandOptions options, CancellationToken ct)
    {
        InitPlan? plan;
        if (!string.IsNullOrWhiteSpace(options.Provider))
        {
            if (!TryBuildPlanFromFlags(options, out plan, out var flagError))
            {
                await Console.Error.WriteLineAsync($"orkeon init: {flagError}").ConfigureAwait(false);
                return Program.ExitScriptError;
            }
        }
        else
        {
            var input = options.InputOverride;
            if (input is null && Console.IsInputRedirected)
            {
                await Console.Error.WriteLineAsync(
                    "orkeon init: stdin is not interactive — pass --provider " +
                    "(ollama | docker-model-runner | openai | custom | none) and related flags.").ConfigureAwait(false);
                return Program.ExitScriptError;
            }

            plan = RunWizard(input ?? Console.In, options);
            if (plan is null)
            {
                await Console.Error.WriteLineAsync("orkeon init: aborted (end of input).").ConfigureAwait(false);
                return Program.ExitScriptError;
            }
        }

        var target = ResolveTargetPath(options.OutputPath);
        if (File.Exists(target) && !options.Force)
        {
            await Console.Error.WriteLineAsync(
                $"orkeon init: {target} already exists — pass --force to overwrite it.").ConfigureAwait(false);
            return Program.ExitScriptError;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, BuildJson(plan), ct).ConfigureAwait(false);

        Console.WriteLine($"Wrote {target}");
        PrintGuidance(plan);

        if (!options.NoProbe && plan.Provider != "none" && plan.BaseUrl is not null)
            await ProbeAsync(plan, ct).ConfigureAwait(false);

        return Program.ExitOk;
    }

    // ── plan building ───────────────────────────────────────────────────────

    private static bool TryBuildPlanFromFlags(
        InitCommandOptions options,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out InitPlan? plan,
        out string? error)
    {
        plan = null;
        error = null;

#pragma warning disable CA1308 // lowercase is the canonical flag-value form, not a comparison normalization
        var provider = options.Provider!.Trim().ToLowerInvariant();
#pragma warning restore CA1308
        switch (provider)
        {
            case "ollama":
                plan = new InitPlan
                {
                    Provider = provider,
                    BaseUrl = options.BaseUrl ?? LlmEndpoints.OllamaDefault,
                    Model = options.Model ?? ProviderDefaults.ForProvider("ollama"),
                };
                return true;

            case "docker-model-runner":
                plan = new InitPlan
                {
                    Provider = provider,
                    BaseUrl = options.BaseUrl ?? DockerModelRunnerBaseUrl,
                    Model = options.Model ?? DockerModelRunnerDefaultModel,
                    // The endpoint requires no auth; the template ships the same placeholder.
                    InlineApiKey = options.ApiKey ?? DockerModelRunnerApiKeyPlaceholder,
                };
                return true;

            case "openai":
                plan = new InitPlan
                {
                    Provider = provider,
                    BaseUrl = options.BaseUrl ?? LlmEndpoints.OpenAI,
                    Model = options.Model ?? ProviderDefaults.ForProvider("openai"),
                    InlineApiKey = options.ApiKey,
                    ApiKeyEnvName = options.ApiKey is null ? options.ApiKeyEnv ?? DefaultApiKeyEnv : null,
                };
                return true;

            case "custom":
                if (string.IsNullOrWhiteSpace(options.BaseUrl) || string.IsNullOrWhiteSpace(options.Model))
                {
                    error = "--provider custom requires both --base-url and --model.";
                    return false;
                }
                plan = new InitPlan
                {
                    Provider = provider,
                    BaseUrl = options.BaseUrl,
                    Model = options.Model,
                    InlineApiKey = options.ApiKey,
                    ApiKeyEnvName = options.ApiKey is null ? options.ApiKeyEnv ?? DefaultApiKeyEnv : null,
                };
                return true;

            case "none":
                plan = new InitPlan { Provider = provider };
                return true;

            default:
                error = $"unknown provider '{options.Provider}'. Supported: ollama, docker-model-runner, openai, custom, none.";
                return false;
        }
    }

    // ── interactive wizard ──────────────────────────────────────────────────

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI wizard prompts.")]
    private static InitPlan? RunWizard(TextReader input, InitCommandOptions options)
    {
        Console.WriteLine("Choose your LLM provider:");
        Console.WriteLine($"  1) Ollama                      (local, {LlmEndpoints.OllamaDefault})");
        Console.WriteLine($"  2) Docker Model Runner         (local, {DockerModelRunnerBaseUrl})");
        Console.WriteLine($"  3) OpenAI                      ({LlmEndpoints.OpenAI})");
        Console.WriteLine("  4) Other OpenAI-compatible     (DeepSeek, GLM, Mistral, …)");
        Console.WriteLine("  5) None / offline              (echo provider — configure later)");
        Console.Write("Selection [1-5]: ");

        var choice = input.ReadLine()?.Trim();
        return choice switch
        {
            "1" => WizardPreset(input, "ollama", LlmEndpoints.OllamaDefault,
                ProviderDefaults.ForProvider("ollama"), needsKey: false,
                hint: "Tip: `orkeon llm models --provider ollama` lists the models your Ollama serves."),
            "2" => new InitPlan
            {
                Provider = "docker-model-runner",
                BaseUrl = DockerModelRunnerBaseUrl,
                Model = AskWithDefault(input, "Model", DockerModelRunnerDefaultModel),
                InlineApiKey = DockerModelRunnerApiKeyPlaceholder,
            },
            "3" => WizardPreset(input, "openai", LlmEndpoints.OpenAI,
                ProviderDefaults.ForProvider("openai"), needsKey: true, hint: null),
            "4" => WizardCustom(input),
            "5" => new InitPlan { Provider = "none" },
            null => null, // EOF — aborted
            _ => RunWizardRetry(input, options),
        };
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI wizard prompts.")]
    private static InitPlan? RunWizardRetry(TextReader input, InitCommandOptions options)
    {
        Console.WriteLine("Please answer 1, 2, 3, 4 or 5.");
        return RunWizard(input, options);
    }

    private static InitPlan? WizardPreset(
        TextReader input, string provider, string baseUrl, string? defaultModel, bool needsKey, string? hint)
    {
        if (hint is not null)
            Console.WriteLine(hint);

        var model = AskWithDefault(input, "Model", defaultModel ?? "");
        if (model is null) return null;

        var plan = new InitPlan { Provider = provider, BaseUrl = baseUrl, Model = model };
        return needsKey ? WizardAskKey(input, plan) : plan;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI wizard prompts.")]
    private static InitPlan? WizardCustom(TextReader input)
    {
        Console.Write("Base URL (OpenAI-compatible endpoint): ");
        var baseUrl = input.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl)) return null;

        Console.Write("Model: ");
        var model = input.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(model)) return null;

        return WizardAskKey(input, new InitPlan { Provider = "custom", BaseUrl = baseUrl, Model = model });
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI wizard prompts.")]
    private static InitPlan? WizardAskKey(TextReader input, InitPlan plan)
    {
        Console.Write($"Reference the API key from an environment variable (recommended) [Y/n]: ");
        var answer = input.ReadLine()?.Trim();
        if (answer is null) return null;

        if (answer.Length == 0 || answer.StartsWith('y') || answer.StartsWith('Y'))
        {
            var env = AskWithDefault(input, "Environment variable", DefaultApiKeyEnv);
            return env is null ? null : plan with { ApiKeyEnvName = env };
        }

        Console.WriteLine("WARNING: the key will be stored in plain text in the generated file.");
        Console.Write("API key: ");
        var key = input.ReadLine()?.Trim();
        return string.IsNullOrWhiteSpace(key) ? null : plan with { InlineApiKey = key };
    }

    private static string? AskWithDefault(TextReader input, string label, string defaultValue)
    {
        Console.Write(defaultValue.Length > 0 ? $"{label} [{defaultValue}]: " : $"{label}: ");
        var line = input.ReadLine();
        if (line is null) return null;
        var trimmed = line.Trim();
        return trimmed.Length > 0 ? trimmed : defaultValue;
    }

    // ── file writing ────────────────────────────────────────────────────────

    private static string ResolveTargetPath(string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            return RunnerSettings.GetGlobalSettingsPath();

        var full = Path.GetFullPath(outputPath);
        return Directory.Exists(full) ? Path.Combine(full, "appsettings.json") : full;
    }

    private static string BuildJson(InitPlan plan)
    {
        // Insertion order is preserved — the generated file reads naturally.
        var root = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (plan.Provider == "none")
        {
            // JSON has no comments; the note travels in a harmless extra key instead.
            root["_comment"] =
                "No LLM configured: Orkeon falls back to the <undefined-llm> echo provider. " +
                "Run `orkeon init` again to configure one.";
        }
        else
        {
            var llm = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Model"] = plan.Model,
                ["BaseUrl"] = plan.BaseUrl,
            };
            if (plan.InlineApiKey is not null)
                llm["ApiKey"] = plan.InlineApiKey;
            root["Llm"] = llm;
        }

        return JsonSerializer.Serialize(root, s_indentedJson) + Environment.NewLine;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI diagnostic messages.")]
    private static void PrintGuidance(InitPlan plan)
    {
        if (plan.Provider == "none")
        {
            Console.WriteLine(
                "No LLM configured: runs will use the <undefined-llm> echo provider. " +
                "Run `orkeon init` again when you are ready to configure one.");
            return;
        }

        if (plan.ApiKeyEnvName is { } envName)
        {
            Console.WriteLine($"API key: referenced from the environment — set it with: export {envName}=<your-key>");
            if (!string.Equals(envName, DefaultApiKeyEnv, StringComparison.Ordinal))
            {
                Console.WriteLine(
                    $"Note: the Orkeon runtime reads `{DefaultApiKeyEnv}` natively; " +
                    $"`{envName}` is only used by `orkeon init`/`orkeon llm` probes.");
            }
        }
        else if (plan.InlineApiKey is not null && plan.InlineApiKey != DockerModelRunnerApiKeyPlaceholder)
        {
            Console.Error.WriteLine(
                "WARNING: the API key is stored in plain text in the generated file. " +
                "Prefer --api-key-env (the file then references an environment variable).");
        }
    }

    // ── connectivity probe (reuses the `llm models` catalogue plumbing) ─────

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI diagnostic messages.")]
    private static async Task ProbeAsync(InitPlan plan, CancellationToken ct)
    {
        // ollama has its own catalogue dialect; every other preset is OpenAI-compatible.
        var catalogKey = plan.Provider == "ollama" ? "ollama" : "openai";
        var apiKey = plan.InlineApiKey
            ?? Environment.GetEnvironmentVariable(plan.ApiKeyEnvName ?? DefaultApiKeyEnv);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            var models = await LlmCatalogClient
                .ListAsync(client, catalogKey, plan.BaseUrl!.TrimEnd('/'), apiKey, ct)
                .ConfigureAwait(false);
            Console.WriteLine($"Probe OK — endpoint serves {models.Count} model(s).");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is HttpRequestException or NotSupportedException or System.Text.Json.JsonException
            or TaskCanceledException)
        {
            await Console.Error.WriteLineAsync(
                $"WARNING: connectivity probe failed ({ex.Message}). " +
                "The configuration was written anyway — start the endpoint and retry with `orkeon doctor`.")
                .ConfigureAwait(false);
        }
    }
}
