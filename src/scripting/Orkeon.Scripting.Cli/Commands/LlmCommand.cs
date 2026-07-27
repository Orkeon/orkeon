using System.Globalization;
using CommandLine;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;

namespace Orkeon.Scripting.Cli.Commands;

/// <summary>Options for <c>orkeon llm probe</c>.</summary>
[Verb("probe", HelpText = "Exercise the LLM test protocol against a live provider and archive the trace.")]
internal sealed class LlmProbeCommandOptions
{
    /// <summary>Provider key, as accepted by <c>LlmProviderFactory</c>.</summary>
    [Option('p', "provider", Required = true,
        HelpText = "Provider: openai | anthropic | ollama | azure | groq | together | qwen | deepseek | kimi | mistral | huggingface | zai.")]
    public string Provider { get; set; } = "";

    /// <summary>Model identifier; defaults to the provider's own default when omitted.</summary>
    [Option('m', "model", Required = false, HelpText = "Model identifier. Defaults to the provider's default model.")]
    public string? Model { get; set; }

    /// <summary>Base URL override (required for Azure, optional elsewhere).</summary>
    [Option('u', "base-url", Required = false, HelpText = "Base URL override. Required for Azure OpenAI.")]
    public string? BaseUrl { get; set; }

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

        return await parser.ParseArguments<LlmProbeCommandOptions>(args)
            .MapResult(
                ExecuteProbeAsync,
                _ => Task.FromResult(Program.ExitScriptError))
            .ConfigureAwait(false);
    }

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

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var config = BuildConfig(options, apiKey);

        using var httpClientFactory = new ProbeHttpClientFactory();
        var factory = new LlmProviderFactory(httpClientFactory, NullLoggerFactory.Instance);
        var adapter = factory.Create(options.Provider, config);
        if (adapter is not LlmProviderAdapter typed)
        {
            await Console.Error.WriteLineAsync(
                "orkeon llm probe: the factory returned a provider the probe cannot unwrap.").ConfigureAwait(false);
            return Program.ExitRuntimeError;
        }

        var runner = new LlmProbeRunner(typed.UnderlyingProvider);

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

        var report = LlmProbeRunner.ToMarkdown(
            options.Provider,
            config.Model,
            typeof(LlmProbeRunner).Assembly.GetName().Version?.ToString() ?? "unknown",
            DateTimeOffset.UtcNow,
            results);

        Console.WriteLine(report);

        if (!string.IsNullOrWhiteSpace(options.Archive))
            await ArchiveAsync(options.Archive!, options.Provider, config.Model, report, cts.Token).ConfigureAwait(false);

        // A campaign that reports a failed mode must fail the command: the point is to learn
        // what does not work, and a green exit code would bury it.
        return results.All(r => r.Passed) ? Program.ExitOk : Program.ExitScriptError;
    }

    private static LlmConfig BuildConfig(LlmProbeCommandOptions options, string? apiKey)
    {
        var config = string.IsNullOrWhiteSpace(options.Model)
            ? LlmConfig.Default()
            : LlmConfig.Create(options.Model!);

#pragma warning disable CS0618 // The probe talks to the provider directly, with no secret store.
        config = config with { ApiKey = apiKey };
#pragma warning restore CS0618

        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            config = config with { BaseUrl = new Uri(options.BaseUrl!) };

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
        string directory, string provider, string model, string report, CancellationToken cancellationToken)
    {
        // EXCEPTION-BOOTSTRAP: the probe runs outside a host, so no IFileSystemService exists;
        // the destination is a path the operator passed explicitly.
        Directory.CreateDirectory(directory);

        var safeModel = string.Concat(model.Select(c => char.IsLetterOrDigit(c) || c is '-' or '.' ? c : '_'));
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var path = Path.Combine(directory, $"probe-{provider}-{safeModel}-{stamp}.md");

        await File.WriteAllTextAsync(path, report, cancellationToken).ConfigureAwait(false);
        await Console.Error.WriteLineAsync($"Archived: {path}").ConfigureAwait(false);
    }

    /// <summary>
    /// Minimal factory: the probe wants the provider's real HTTP behaviour, including its
    /// timeouts, so it hands out plain clients rather than the host's configured pipeline.
    /// </summary>
    private sealed class ProbeHttpClientFactory : IHttpClientFactory, IDisposable
    {
        private readonly Dictionary<string, HttpClient> _clients = [];

        // Providers call CreateClient once per request; reusing the instance per name mirrors
        // what the real factory does and keeps a long campaign from opening a socket per mode.
        public HttpClient CreateClient(string name)
        {
            if (_clients.TryGetValue(name, out var existing))
                return existing;

            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            _clients[name] = client;
            return client;
        }

        public void Dispose()
        {
            foreach (var client in _clients.Values)
                client.Dispose();
            _clients.Clear();
        }
    }
}
