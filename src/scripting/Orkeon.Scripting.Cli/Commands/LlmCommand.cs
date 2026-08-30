using System.Globalization;
using CommandLine;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.ToolCalling;

namespace Orkeon.Scripting.Cli.Commands;

/// <summary>Options for <c>orkeon llm probe</c>.</summary>
[Verb("probe", HelpText = "Exercise the LLM test protocol against a live provider and archive the trace.")]
internal sealed class LlmProbeCommandOptions
{
    /// <summary>Provider key, as accepted by <c>LlmProviderFactory</c>.</summary>
    [Option('p', "provider", Required = true,
        HelpText = "Provider: openai | anthropic | ollama | azure | together | qwen | deepseek | kimi | mistral | huggingface | zai | gemini | grok.")]
    public string Provider { get; set; } = "";

    /// <summary>Model identifier; defaults to the provider's own default when omitted.</summary>
    [Option('m', "model", Required = false, HelpText = "Model identifier. Defaults to the provider's default model.")]
    public string? Model { get; set; }

    /// <summary>Base URL override (required for Azure, optional elsewhere).</summary>
    [Option('u', "base-url", Required = false, HelpText = "Base URL override. Required for Azure OpenAI.")]
    public string? BaseUrl { get; set; }

    /// <summary>Azure <c>api-version</c>, for the deployment-mode URL. Omitted in v1 GA mode.</summary>
    [Option("api-version", Required = false,
        HelpText = "Azure OpenAI api-version (deployment mode). Omit to exercise the v1 GA surface.")]
    public string? ApiVersion { get; set; }

    /// <summary>
    /// Workspace the requests act in. Anthropic's identity-linked API keys refuse every
    /// request without it (2026-08-30); classic keys need none. An identifier, not a secret.
    /// </summary>
    [Option("workspace-id", Required = false,
        HelpText = "Workspace id for workspace-scoped keys (Anthropic identity-linked keys require it).")]
    public string? WorkspaceId { get; set; }

    /// <summary>
    /// Base thinking-effort hint, for models that demand one. <c>gpt-5.6-sol</c> refuses
    /// function tools on chat/completions unless reasoning is explicitly off (2026-08-30) —
    /// same shape as the pinned temperature, same remedy: the catalogue declares it, the
    /// report prints it. M7 keeps probing thinking with its own explicit effort.
    /// </summary>
    [Option("thinking-effort", Required = false,
        HelpText = "Base reasoning-effort hint (e.g. none). For models that refuse tools while reasoning; M7 still probes thinking explicitly.")]
    public string? ThinkingEffort { get; set; }

    /// <summary>
    /// Effort value M7 probes with, for models whose supported set excludes the default
    /// "low" (mistral-medium-2604 accepts only high or none, 2026-08-30).
    /// </summary>
    [Option("m7-effort", Required = false,
        HelpText = "Reasoning effort M7 probes with (default low). For models whose supported set excludes low.")]
    public string? M7Effort { get; set; }

    /// <summary>Environment variable holding the API key. Never the key itself.</summary>
    [Option('k', "api-key-env", Required = false, Default = "ORKEON_LLM_API_KEY",
        HelpText = "Name of the environment variable holding the API key. The key itself is never accepted on the command line.")]
    public string ApiKeyEnv { get; set; } = "ORKEON_LLM_API_KEY";

    /// <summary>Modes to exercise, comma-separated. Defaults to every supported mode.</summary>
    [Option("modes", Required = false,
        HelpText = "Comma-separated protocol modes (e.g. M1,M2,M8). Defaults to every mode this harness supports.")]
    public string? Modes { get; set; }

    /// <summary>Directory receiving the archived campaign report.</summary>
    [Option("archive", Required = false,
        HelpText = "Directory to write the campaign report to. Nothing is archived when omitted.")]
    public string? Archive { get; set; }

    /// <summary>Report shape written to standard output.</summary>
    [Option("format", Required = false, Default = "md",
        HelpText = "Output format: md (default, human-readable) or json (for the campaign scripts).")]
    public string Format { get; set; } = "md";

    /// <summary>Short commit of the tree under test, supplied by the caller.</summary>
    [Option("commit", Required = false,
        HelpText = "Short commit of the tree under test, recorded in the report. The harness does not shell out to git.")]
    public string? Commit { get; set; }

    /// <summary>Per-request timeout. Generous by default — a cold local model has to load first.</summary>
    [Option("timeout", Required = false, Default = 180,
        HelpText = "Per-request timeout in seconds (default 180). The first call to a local model pays for loading it into memory.")]
    public int TimeoutSeconds { get; set; } = 180;

    /// <summary>Sampling temperature. Zero by default: a conformance probe measures plumbing, not creativity.</summary>
    /// <remarks>
    /// Overridable because a handful of reasoning models reject any value but their own default,
    /// and refusing the whole campaign over a sampling knob would be absurd.
    /// </remarks>
    [Option("temperature", Required = false, Default = 0.0d,
        HelpText = "Sampling temperature (default 0). Raise it only for a model that rejects a pinned temperature.")]
    public double Temperature { get; set; }
}

/// <summary>Options for <c>orkeon llm models</c>.</summary>
[Verb("models", HelpText = "List the models a provider currently serves, optionally filtered by a glob.")]
internal sealed class LlmModelsCommandOptions
{
    /// <summary>Provider key, as accepted by <c>LlmProviderFactory</c>.</summary>
    [Option('p', "provider", Required = true, HelpText = "Provider key.")]
    public string Provider { get; set; } = "";

    /// <summary>Base URL override; defaults to the provider's own endpoint.</summary>
    [Option('u', "base-url", Required = false, HelpText = "Base URL override.")]
    public string? BaseUrl { get; set; }

    /// <summary>Environment variable holding the API key. Never the key itself.</summary>
    [Option('k', "api-key-env", Required = false, Default = "ORKEON_LLM_API_KEY",
        HelpText = "Name of the environment variable holding the API key.")]
    public string ApiKeyEnv { get; set; } = "ORKEON_LLM_API_KEY";

    /// <summary>Shell-style glob restricting the listing.</summary>
    [Option('f', "filter", Required = false, HelpText = "Shell-style glob, e.g. 'gpt-5.6-*'.")]
    public string? Filter { get; set; }

    /// <summary>Emit a JSON array instead of one identifier per line.</summary>
    [Option("json", Required = false, HelpText = "Emit a JSON array instead of one identifier per line.")]
    public bool Json { get; set; }
}

/// <summary>
/// The <c>orkeon llm</c> verb group: campaign harness for the provider test protocol (LLM-08).
/// </summary>
/// <remarks>
/// The campaigns themselves consume real credits on real accounts, so this command is the
/// harness, not the campaign: it makes a run reproducible, archivable and comparable, and
/// leaves the decision to spend to whoever owns the account.
/// </remarks>
internal static class LlmCommand
{
    /// <summary>Dispatches an <c>orkeon llm …</c> invocation.</summary>
    public static async Task<int> DispatchAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        using var parser = new Parser(s =>
        {
            s.HelpWriter = Console.Out;
            s.CaseInsensitiveEnumValues = true;
        });

        return await parser.ParseArguments<LlmProbeCommandOptions, LlmModelsCommandOptions>(args)
            .MapResult(
                (LlmProbeCommandOptions o) => ExecuteProbeAsync(o),
                (LlmModelsCommandOptions o) => ExecuteModelsAsync(o),
                _ => Task.FromResult(Program.ExitScriptError))
            .ConfigureAwait(false);
    }

    // ── orkeon llm models ───────────────────────────────────────────────────

    private static async Task<int> ExecuteModelsAsync(LlmModelsCommandOptions options)
    {
        var apiKey = Environment.GetEnvironmentVariable(options.ApiKeyEnv);

        using var cts = CreateInterruptibleTokenSource();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        IReadOnlyList<string> models;
        try
        {
            var baseUrl = LlmCatalogClient.ResolveBaseUrl(options.Provider, options.BaseUrl);
            var all = await LlmCatalogClient
                .ListAsync(client, options.Provider, baseUrl, apiKey, cts.Token).ConfigureAwait(false);
            models = LlmCatalogClient.Filter(all, options.Filter);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return Program.ExitCancelled;
        }
        catch (Exception ex) when (
            ex is NotSupportedException or HttpRequestException or System.Text.Json.JsonException
            // An HttpClient timeout surfaces as a TaskCanceledException nobody asked for.
            // Reporting it as "cancelled" would tell the operator they pressed Ctrl+C.
            or TaskCanceledException)
        {
            // A missing catalogue is a documented, recoverable case: the campaign scripts fall
            // back to the model list declared in their JSON. Say so instead of just failing.
            await Console.Error.WriteLineAsync($"orkeon llm models: {ex.Message}").ConfigureAwait(false);
            return Program.ExitScriptError;
        }

        if (options.Json)
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(models));
        else
            foreach (var model in models)
                Console.WriteLine(model);

        return models.Count > 0 ? Program.ExitOk : Program.ExitScriptError;
    }

    // ── orkeon llm probe ────────────────────────────────────────────────────

    private static async Task<int> ExecuteProbeAsync(LlmProbeCommandOptions options)
    {
        var apiKey = Environment.GetEnvironmentVariable(options.ApiKeyEnv);
        var needsKey = !string.Equals(options.Provider, "ollama", StringComparison.OrdinalIgnoreCase);
        if (needsKey && string.IsNullOrWhiteSpace(apiKey))
        {
            await Console.Error.WriteLineAsync(
                $"orkeon llm probe: no API key found in ${options.ApiKeyEnv}. " +
                "Export it, or point --api-key-env at the variable that holds it. " +
                "Keys are never accepted as a command-line argument.").ConfigureAwait(false);
            return Program.ExitScriptError;
        }

        if (!TryParseModes(options.Modes, out var modes, out var badMode))
        {
            await Console.Error.WriteLineAsync(
                $"orkeon llm probe: unknown mode '{badMode}'. Supported: " +
                string.Join(", ", LlmProbeRunner.SupportedModes)).ConfigureAwait(false);
            return Program.ExitScriptError;
        }

        var wantsJson = string.Equals(options.Format, "json", StringComparison.OrdinalIgnoreCase);
        if (!wantsJson && !string.Equals(options.Format, "md", StringComparison.OrdinalIgnoreCase))
        {
            await Console.Error.WriteLineAsync(
                $"orkeon llm probe: unknown format '{options.Format}'. Supported: md, json.").ConfigureAwait(false);
            return Program.ExitScriptError;
        }

        using var cts = CreateInterruptibleTokenSource();
        var config = BuildConfig(options, apiKey);

        using var httpClientFactory = new CliHttpClientFactory();
        var factory = new LlmProviderFactory(httpClientFactory, NullLoggerFactory.Instance);
        var adapter = factory.Create(options.Provider, config);
        if (adapter is not LlmProviderAdapter typed)
        {
            await Console.Error.WriteLineAsync(
                "orkeon llm probe: the factory returned a provider the probe cannot unwrap.").ConfigureAwait(false);
            return Program.ExitRuntimeError;
        }

        var runner = new LlmProbeRunner(typed.UnderlyingProvider, ResolveToolCallParser(options.Provider))
        {
            M7ThinkingEffort = string.IsNullOrWhiteSpace(options.M7Effort) ? "low" : options.M7Effort!,
        };

        IReadOnlyList<LlmProbeResult> results;
        try
        {
            results = await runner.RunAsync(config, modes, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Program.ExitCancelled;
        }
        catch (NotSupportedException ex)
        {
            await Console.Error.WriteLineAsync($"orkeon llm probe: {ex.Message}").ConfigureAwait(false);
            return Program.ExitScriptError;
        }

        var context = BuildContext(options, config);
        var report = wantsJson
            ? LlmProbeReport.ToJson(context, results)
            : LlmProbeReport.ToMarkdown(context, results);

        Console.WriteLine(report);

        if (!string.IsNullOrWhiteSpace(options.Archive))
        {
            await ArchiveAsync(options.Archive!, options.Provider, config.Model, report, wantsJson, cts.Token)
                .ConfigureAwait(false);
        }

        // A campaign that reports a failed mode must fail the command: the point is to learn
        // what does not work, and a green exit code would bury it. A not-applicable mode is
        // not a failure — nothing was exercised, so there is nothing to act on.
        return results.All(r => r.Outcome != LlmProbeOutcome.Failed) ? Program.ExitOk : Program.ExitScriptError;
    }

    private static LlmProbeContext BuildContext(LlmProbeCommandOptions options, LlmConfig config) =>
        new(
            Provider: options.Provider,
            Model: config.Model,
            EndpointHost: ResolveEndpointHost(options, config),
            OrkeonVersion: LlmProbeReport.ResolveVersion(),
            Commit: options.Commit ?? "",
            TimestampUtc: DateTimeOffset.UtcNow,
            Temperature: config.Temperature,
            ThinkingEffort: config.Thinking?.Effort);

    /// <summary>
    /// The host, never the full URL: a base URL can carry a resource name, a workspace or a
    /// query string, and none of that belongs in an archived, versioned report.
    /// </summary>
    private static string ResolveEndpointHost(LlmProbeCommandOptions options, LlmConfig config)
    {
        if (config.BaseUrl is { } explicitUrl)
            return explicitUrl.Host;

        try
        {
            return new Uri(LlmCatalogClient.ResolveBaseUrl(options.Provider, options.BaseUrl)).Host;
        }
        catch (Exception ex) when (ex is NotSupportedException or UriFormatException)
        {
            return "unknown";
        }
    }

    /// <summary>
    /// Picks the parser matching the provider's wire dialect, exactly as the factory picks the
    /// strategy it hands the provider: Anthropic speaks its own, everything else speaks OpenAI.
    /// </summary>
    private static IToolCallParser ResolveToolCallParser(string provider) =>
        string.Equals(provider, "anthropic", StringComparison.OrdinalIgnoreCase)
            ? new AnthropicToolCallingStrategy().Parser
            : new OpenAIToolCallingStrategy(NullLogger<OpenAIToolCallParser>.Instance).Parser;

    private static CancellationTokenSource CreateInterruptibleTokenSource()
    {
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        return cts;
    }

    internal static LlmConfig BuildConfig(LlmProbeCommandOptions options, string? apiKey)
    {
        // Falling back to LlmConfig.Default() would send OpenAI's default model to whichever
        // provider was named — a campaign against Kimi would silently measure "gpt-5.6-sol".
        var model = string.IsNullOrWhiteSpace(options.Model)
            ? ProviderDefaults.ForProvider(options.Provider)
            : options.Model;

        var config = string.IsNullOrWhiteSpace(model)
            ? LlmConfig.Default()
            : LlmConfig.Create(model!);

#pragma warning disable CS0618 // The probe talks to the provider directly, with no secret store.
        config = config with { ApiKey = apiKey };
#pragma warning restore CS0618

        // LlmConfig defaults to 30 seconds and the provider hands that straight to HttpClient,
        // overriding whatever the factory set. A cold Ollama model spends longer than that just
        // loading, so M1 failed on a timeout and reported it as a provider fault (2026-08-01).
        config = config with { TimeoutSeconds = options.TimeoutSeconds };

        // LlmConfig defaults to 0.7, so the harness was measuring conformance through creative
        // sampling: three consecutive M2 runs against the same llama3.2 returned ❌ ✅ ❌
        // (2026-08-01). A flickering verdict is worse than a red one — it invites reading noise
        // as a defect. LlmConfig.Seed would pin this further, but no HTTP provider puts it on the
        // wire, and setting a field that goes nowhere is the silent drop this codebase refuses.
        config = config with { Temperature = options.Temperature };

        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            config = config with { BaseUrl = new Uri(options.BaseUrl!) };

        if (!string.IsNullOrWhiteSpace(options.ApiVersion))
            config = config with { ApiVersion = options.ApiVersion };

        // Anthropic's identity-linked keys are refused without their workspace id — the
        // 2026-08-30 campaign could not place a single call until this existed.
        if (!string.IsNullOrWhiteSpace(options.WorkspaceId))
            config = config with { WorkspaceId = options.WorkspaceId };

        // Some models demand an explicit reasoning effort before they accept function tools
        // (gpt-5.6-sol, 2026-08-30). Base config only: M7 overrides with its own effort.
        if (!string.IsNullOrWhiteSpace(options.ThinkingEffort))
            config = config with { Thinking = new LlmThinkingConfig { Effort = options.ThinkingEffort } };

        return config;
    }

    private static bool TryParseModes(
        string? raw, out IReadOnlyList<LlmProbeMode> modes, out string? invalid)
    {
        invalid = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            modes = LlmProbeRunner.SupportedModes;
            return true;
        }

        var parsed = new List<LlmProbeMode>();
        foreach (var token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<LlmProbeMode>(token, ignoreCase: true, out var mode)
                || !LlmProbeRunner.SupportedModes.Contains(mode))
            {
                invalid = token;
                modes = [];
                return false;
            }

            parsed.Add(mode);
        }

        modes = parsed;
        return true;
    }

    /// <summary>
    /// Writes the report next to the other campaign traces. The report is built from observed
    /// behaviour only — it never contains the key, which exists solely in the environment and
    /// in the in-memory config.
    /// </summary>
    private static async Task ArchiveAsync(
        string directory, string provider, string model, string report, bool json,
        CancellationToken cancellationToken)
    {
        // EXCEPTION-BOOTSTRAP. The probe runs outside a host, so no IFileSystemService exists  —
        // the destination is a path the operator passed explicitly.
        Directory.CreateDirectory(directory);

        var safeModel = string.Concat(model.Select(c => char.IsLetterOrDigit(c) || c is '-' or '.' ? c : '_'));
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var path = Path.Combine(directory, $"probe-{provider}-{safeModel}-{stamp}.{(json ? "json" : "md")}");

        await File.WriteAllTextAsync(path, report, cancellationToken).ConfigureAwait(false);
        await Console.Error.WriteLineAsync($"Archived: {path}").ConfigureAwait(false);
    }
}
