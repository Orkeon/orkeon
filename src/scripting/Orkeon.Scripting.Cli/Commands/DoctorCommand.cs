using Orkeon.Constants.Configuration;
using Orkeon.Constants.FileSystem;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Hosting;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Profiles;
using Orkeon.Rag.Onnx.Model;
using Orkeon.Scripting.Toolchain;

namespace Orkeon.Scripting.Cli.Commands;

/// <summary>Options for <c>orkeon doctor</c>.</summary>
internal sealed class DoctorCommandOptions
{
    /// <summary>Emit the machine-readable report instead of the table.</summary>
    [Option("json", Required = false, Default = false,
        HelpText = "Emit a JSON array of {check, status, detail} instead of the human-readable table (CI).")]
    public bool Json { get; set; }

    /// <summary>Test seam: overrides <see cref="Directory.GetCurrentDirectory"/>.</summary>
    internal string? WorkingDirectoryOverride { get; set; }

    /// <summary>Test seam: overrides <see cref="AppContext.BaseDirectory"/> for the install channel.</summary>
    internal string? InstallDirectoryOverride { get; set; }
}

/// <summary>One diagnostic result. The <c>--json</c> schema is a CI contract — keep it stable.</summary>
internal sealed record DoctorCheckResult
{
    [JsonPropertyName("check")]
    public required string Check { get; init; }

    /// <summary>"ok" | "warn" | "fail".</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("detail")]
    public required string Detail { get; init; }
}

/// <summary>
/// <c>orkeon doctor</c> — installation diagnostic (WIN-03). Says in under 15 seconds what
/// works and what is missing, as a ✅/⚠️/❌ table or <c>--json</c> for CI. Exit codes are
/// stable: 0 = everything green or warnings only, 1 = at least one failing check.
/// Reuses the existing plumbing (<see cref="RunnerSettings"/>, <see cref="LlmCatalogClient"/>,
/// <see cref="RunnerExecution.IsLlmEndpointReachableAsync"/>, <see cref="EsbuildTranspiler"/>,
/// <see cref="RunnerHost"/>) — no duplicated HTTP client, no duplicated resolution chain and no
/// second judgement of the settings: <c>runner-settings</c> builds the host <c>orkeon run</c> builds
/// and passes its start validation (GAP-40). Two checks touch the disk, outside the working
/// directory but for the first: the workspace write, which cleans up after itself, and
/// <c>runner-settings</c>, whose host creates its sandbox directory under the temp directory and
/// deletes it — and, when an e-mail account signs in with OAuth, the token directory a run creates.
/// </summary>
internal static class DoctorCommand
{
    private const string StatusOk = "ok";
    private const string StatusWarn = "warn";
    private const string StatusFail = "fail";

    private static readonly TimeSpan TcpProbeTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CatalogTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan EsbuildVersionTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The grammar libraries <c>LanguageRegistry</c> needs, plus the core runtime library —
    /// aligned on the publish-pruning whitelist in <c>src/Directory.Build.targets</c>
    /// (<c>_OrkeonTreeSitterKeptGrammars</c>). Note <c>tree-sitter-tsx</c>: the package ships
    /// it as a native library distinct from <c>tree-sitter-typescript</c>.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>Orkeon.Analysis.TreeSitter.LanguageRegistry</c> (which keys languages, not
    /// library names). This is a tolerant ⚠️ presence check, so drift only softens a warning.
    /// </remarks>
    internal static readonly string[] TreeSitterLibraries =
    [
        "tree-sitter",
        "tree-sitter-typescript",
        "tree-sitter-tsx",
        "tree-sitter-python",
        "tree-sitter-c-sharp",
        "tree-sitter-go",
        "tree-sitter-rust",
    ];

    /// <summary>Maps the concrete provider type the factory inferred onto its catalogue key.</summary>
    private static readonly Dictionary<string, string> CatalogKeyByProviderType = new(StringComparer.Ordinal)
    {
        ["OllamaLlmProvider"] = "ollama",
        ["OpenAIProvider"] = "openai",
        ["AnthropicLlmProvider"] = "anthropic",
        ["GrokLlmProvider"] = "grok",
        ["MiniMaxLlmProvider"] = "minimax",
        ["DeepSeekLlmProvider"] = "deepseek",
        ["KimiLlmProvider"] = "kimi",
        ["QwenLlmProvider"] = "qwen",
        ["MistralLlmProvider"] = "mistral",
        ["TogetherAiLlmProvider"] = "together",
        ["HuggingFaceLlmProvider"] = "huggingface",
        ["ZaiLlmProvider"] = "zai",
        ["OpenRouterLlmProvider"] = "openrouter",
        ["MammouthLlmProvider"] = "mammouth",
        // AzureOpenAILlmProvider intentionally absent: deployments, no public catalogue.
    };

    /// <summary>Dispatches an <c>orkeon doctor …</c> invocation (args already stripped of the verb).</summary>
    public static async Task<int> DispatchAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        using var parser = new Parser(s =>
        {
            s.HelpWriter = Console.Out;
            s.CaseInsensitiveEnumValues = true;
        });

        return await parser.ParseArguments<DoctorCommandOptions>(args)
            .MapResult(
                (DoctorCommandOptions o) => ExecuteAsync(o),
                _ => Task.FromResult(Program.ExitScriptError))
            .ConfigureAwait(false);
    }

    /// <summary>Runs every check; returns 0 (green/warnings) or 1 (at least one failure).</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Top-level CLI fault barrier, mirroring the run/rag verbs' exit-code contract.")]
    public static async Task<int> ExecuteAsync(DoctorCommandOptions options)
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
            var cwd = Path.GetFullPath(options.WorkingDirectoryOverride ?? Directory.GetCurrentDirectory());
            var installDirectory = options.InstallDirectoryOverride ?? AppContext.BaseDirectory;
            var results = await RunChecksAsync(cwd, installDirectory, cts.Token).ConfigureAwait(false);

            if (options.Json)
                Console.WriteLine(JsonSerializer.Serialize(results));
            else
                PrintTable(results);

            return results.Any(r => r.Status == StatusFail) ? Program.ExitScriptError : Program.ExitOk;
        }
        catch (OperationCanceledException)
        {
            return Program.ExitCancelled;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"orkeon doctor: unexpected error [{ex.GetType().FullName}]: {ex.Message}")
                .ConfigureAwait(false);
            return Program.ExitRuntimeError;
        }
    }

    private static async Task<IReadOnlyList<DoctorCheckResult>> RunChecksAsync(
        string cwd, string installDirectory, CancellationToken ct)
    {
        var llm = ReadLlmContext(cwd);

        return
        [
            CheckDotnetRuntime(),
            CheckInstallChannel(installDirectory),
            CheckAppSettings(llm),
            CheckLlmConfig(llm),
            CheckLlmProfiles(llm),
            .. CheckLlmProfileKeys(llm),
            .. CheckRunnerSettings(llm),
            await CheckLlmReachabilityAsync(llm, ct).ConfigureAwait(false),
            await CheckEsbuildAsync(llm.Configuration, ct).ConfigureAwait(false),
            CheckLocalEmbeddings(),
            CheckOnnxReranker(),
            CheckTreeSitterGrammars(),
            CheckWorkspaceWrite(cwd),
        ];
    }

    // ── configuration context (settings resolution replayed once, reused by 3 checks) ──

    private sealed record LlmContext
    {
        public string? SettingsPath { get; init; }
        public bool HasLlmSection { get; init; }
        public string? Model { get; init; }
        public string? BaseUrl { get; init; }
        /// <summary>The default's key as a run resolves it — the configuration, else the variable <c>ApiKeyEnvVar</c> names.</summary>
        public string? ApiKey { get; init; }
        /// <summary>Where that key comes from (<see cref="LlmSettings.DescribeApiKey(IConfigurationSection)"/>) — never the key.</summary>
        public string? ApiKeySource { get; init; }
        /// <summary>Why the <c>Llm</c> section cannot be read: the runner would refuse to start on it.</summary>
        public string? SettingsError { get; init; }
        public string? ProviderTypeName { get; init; }
        public string? ProviderDisplayName { get; init; }
        /// <summary>Each profile of <c>Llm:Profiles</c> and where its key comes from.</summary>
        public IReadOnlyList<(string Name, string ApiKeySource)> Profiles { get; init; } = [];
        /// <summary>Why <c>Llm:Profiles</c> cannot be read: the runner would refuse to start on it.</summary>
        public string? ProfilesError { get; init; }
        /// <summary>The <c>ApiKeyEnvVar</c> paths naming a variable set nowhere (<see cref="LlmSettings.UnresolvedApiKeyReferences(IConfiguration)"/>).</summary>
        public IReadOnlyList<string> UnresolvedReferences { get; init; } = [];
        /// <summary>The variables without a prefix, the settings file, the <c>ORKEON_</c> variables: what a runner reads.</summary>
        public required IConfiguration Configuration { get; init; }
    }

    private static LlmContext ReadLlmContext(string cwd)
    {
        // Same chain as every runner (explicit → local → walk-up → global), quiet: doctor
        // reports the outcome itself instead of the resolver's stderr warning.
        var settingsPath = RunnerSettings.ResolveSettingsPath(null, cwd, quiet: true);

        var configuration = RunnerSettings.ReadConfiguration(settingsPath);
        var context = ReadLlmProfiles(new LlmContext { SettingsPath = settingsPath, Configuration = configuration });

        // The runner's own reading (WIN-01): no default — no section, profiles alone, every
        // value blank — is the echo provider.
        if (!LlmSettings.HasDefault(configuration))
            return context;

        var section = configuration.GetSection(ConfigurationKeys.LlmSection);
        var model = Blank(section["Model"]);
        var baseUrl = Blank(section["BaseUrl"]);
        context = context with { HasLlmSection = true, Model = model, BaseUrl = baseUrl };

        // The key as a run resolves it (STUDIO-49) — LlmSettings, the reader the runners use,
        // rather than a second reading of Llm:ApiKey that would miss ApiKeyEnvVar.
        LlmConfig settings;
        try
        {
            settings = LlmSettings.ReadDefault(configuration);
            context = context with { ApiKey = ApiKeyOf(settings), ApiKeySource = LlmSettings.DescribeApiKey(section) };
        }
        catch (InvalidOperationException ex)
        {
            return context with { SettingsError = ex.Message };
        }

        // Reuse the factory's own inference (BaseUrl → model → key) instead of duplicating it.
        try
        {
            var config = LlmConfig.Create(model ?? "gpt-4") with
            {
                BaseUrl = settings.BaseUrl,
                ApiKey = context.ApiKey,
            };
            using var httpFactory = new CliHttpClientFactory();
            var factory = new LlmProviderFactory(httpFactory, NullLoggerFactory.Instance);
            if (factory.Create(config) is LlmProviderAdapter adapter)
            {
                var typeName = adapter.UnderlyingProvider.GetType().Name;
                context = context with
                {
                    ProviderTypeName = typeName,
                    ProviderDisplayName = ToDisplayName(typeName),
                };
            }
        }
        catch (Exception ex) when (ex is NotSupportedException or UriFormatException or ArgumentException)
        {
            // Inference failure is reported by the llm-config check, not thrown at the operator.
        }

        return context;
    }

    /// <summary>
    /// The profiles as the runner reads them — validated, so a profile the host would refuse is a
    /// failed check here — with where each key comes from, and the references set nowhere.
    /// </summary>
    private static LlmContext ReadLlmProfiles(LlmContext context)
    {
        try
        {
            var profiles = LlmSettings.ReadProfiles(context.Configuration);
            var section = context.Configuration.GetSection(ConfigurationKeys.LlmSection).GetSection(ConfigurationKeys.LlmProfiles);
            return context with
            {
                Profiles = [.. profiles.Select(p => (p.Name, LlmSettings.DescribeApiKey(section.GetSection(p.Name))))],
                UnresolvedReferences = LlmSettings.UnresolvedApiKeyReferences(context.Configuration),
            };
        }
        catch (InvalidOperationException ex)
        {
            return context with { ProfilesError = ex.Message };
        }
    }

#pragma warning disable CS0618 // ApiKey is the field every provider reads.
    private static string? ApiKeyOf(LlmConfig config) => config.ApiKey;
#pragma warning restore CS0618

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string ToDisplayName(string providerTypeName) =>
        providerTypeName
            .Replace("LlmProvider", "", StringComparison.Ordinal)
            .Replace("Provider", "", StringComparison.Ordinal);

    // ── individual checks ───────────────────────────────────────────────────

    /// <summary>Platform prefix of a native library file name ("lib" everywhere but Windows).</summary>
    private static string NativeLibraryPrefix() => OperatingSystem.IsWindows() ? "" : "lib";

    /// <summary>Platform extension of a native library file name.</summary>
    private static string NativeLibraryExtension()
    {
        if (OperatingSystem.IsWindows())
            return ".dll";

        return OperatingSystem.IsMacOS() ? ".dylib" : ".so";
    }

    /// <summary>
    /// Names the channel this build was installed through and how that channel is updated.
    /// Informative: an installation without a marker is older than the marker, or a build
    /// tree, and is reported as such. Only a marker that exists and names nothing is a warning.
    /// </summary>
    private static DoctorCheckResult CheckInstallChannel(string installDirectory)
    {
        var reading = InstallChannelReader.Read(installDirectory);
        var update = InstallChannelReader.UpdateHint(reading.Channel);
        var origin = reading.Channel == InstallChannels.Unknown
            ? reading.Problem
                ?? $"no {InstallChannels.MarkerFile} marker (an installation older than the marker, or a build tree)"
            : null;

        return new DoctorCheckResult
        {
            Check = "install-channel",
            Status = reading.Problem is null ? StatusOk : StatusWarn,
            Detail = origin is null
                ? $"{reading.Channel} — orkeon {CliUsage.Version}; to update: {update}"
                : $"{reading.Channel} — {origin}; orkeon {CliUsage.Version}; to update: {update}",
        };
    }

    private static DoctorCheckResult CheckDotnetRuntime()
    {
        // Self-contained publishes carry the host resolver next to the app; a
        // framework-dependent deployment resolves it from the shared installation.
        var hostfxr = NativeLibraryPrefix() + "hostfxr" + NativeLibraryExtension();
        var selfContained = File.Exists(Path.Combine(AppContext.BaseDirectory, hostfxr));

        return new DoctorCheckResult
        {
            Check = "dotnet-runtime",
            Status = StatusOk,
            Detail = selfContained
                ? $"embedded (self-contained, {RuntimeInformation.FrameworkDescription})"
                : $"shared runtime ({RuntimeInformation.FrameworkDescription})",
        };
    }

    private static DoctorCheckResult CheckAppSettings(LlmContext llm) => new()
    {
        Check = "appsettings",
        Status = llm.SettingsPath is not null ? StatusOk : StatusWarn,
        Detail = llm.SettingsPath
            ?? "no appsettings.json found (env vars only) — run `orkeon init` to create one",
    };

    private static DoctorCheckResult CheckLlmConfig(LlmContext llm)
    {
        if (!llm.HasLlmSection)
        {
            return new DoctorCheckResult
            {
                Check = "llm-config",
                Status = StatusWarn,
                Detail = "no `Llm` section — runs fall back to the <undefined-llm> echo provider; run `orkeon init`",
            };
        }

        if (llm.SettingsError is { } error)
        {
            // The runner refuses to start on this section: a failure, with the key to fix.
            return new DoctorCheckResult { Check = "llm-config", Status = StatusFail, Detail = error };
        }

        // STUDIO-49: where the key comes from — the configuration, the variable ApiKeyEnvVar
        // names, none —, never the key nor the variable's name; a reference set nowhere warns.
        var unresolved = llm.UnresolvedReferences.Contains(DefaultApiKeyReference, StringComparer.Ordinal);
        var provider = llm.ProviderDisplayName ?? "unrecognised";
        return new DoctorCheckResult
        {
            Check = "llm-config",
            Status = llm.ProviderDisplayName is not null && !unresolved ? StatusOk : StatusWarn,
            Detail = $"provider {provider}, model {llm.Model ?? "(default)"}, endpoint {llm.BaseUrl ?? "(provider default)"}, " +
                     $"API key {llm.ApiKeySource}",
        };
    }

    private static readonly CompositeFormat UnresolvedApiKeyReferenceFormat =
        CompositeFormat.Parse(OperatorMessages.LlmApiKeyReferenceUnresolved);

    /// <summary>The path of the default profile's key reference.</summary>
    private const string DefaultApiKeyReference = ConfigurationKeys.LlmSection + ":" + ConfigurationKeys.LlmApiKeyEnvVar;

    /// <summary>One line covers <c>Llm:Profiles</c>: each profile a crew may name, and where its key comes from.</summary>
    private static DoctorCheckResult CheckLlmProfiles(LlmContext llm)
    {
        if (llm.ProfilesError is { } error)
            return new DoctorCheckResult { Check = "llm-profiles", Status = StatusFail, Detail = error };

        if (llm.Profiles.Count == 0)
            return new DoctorCheckResult { Check = "llm-profiles", Status = StatusOk, Detail = "none configured (Llm:Profiles)" };

        return new DoctorCheckResult
        {
            Check = "llm-profiles",
            Status = StatusOk,
            Detail = $"{llm.Profiles.Count} profile(s): " +
                     string.Join("; ", llm.Profiles.Select(p => $"{p.Name} — API key {p.ApiKeySource}")),
        };
    }

    /// <summary>
    /// One warning line per profile whose <c>ApiKeyEnvVar</c> names a variable set nowhere: every
    /// call on it answers that an API key is required. The path, never the name it holds.
    /// </summary>
    private static IEnumerable<DoctorCheckResult> CheckLlmProfileKeys(LlmContext llm) =>
        llm.UnresolvedReferences
            .Where(reference => !string.Equals(reference, DefaultApiKeyReference, StringComparison.Ordinal))
            .Select(reference => new DoctorCheckResult
            {
                Check = "llm-profile-key",
                Status = StatusWarn,
                Detail = string.Format(CultureInfo.InvariantCulture, UnresolvedApiKeyReferenceFormat, reference),
            });

    /// <summary>
    /// GAP-40: what <c>orkeon run</c> refuses at its start on the same settings file, judged by the
    /// same construction — the guards the run applies to the file's mounts, then the host it builds
    /// (<see cref="RunCommand.AddCliRagServices"/> included, so the ONNX reranker is offered) and its
    /// start validation. One <c>fail</c> line per refusal, each naming its key; one <c>ok</c> line
    /// otherwise. A refusal the build itself raises is the only line, as the run reports it. Skipped
    /// when the <c>Llm</c> section is already refused: the run stops on it first, and the line above
    /// says why.
    /// </summary>
    private static IEnumerable<DoctorCheckResult> CheckRunnerSettings(LlmContext llm)
    {
        const string Check = "runner-settings";

        if (llm.SettingsError is not null || llm.ProfilesError is not null)
        {
            var see = llm.SettingsError is not null ? "llm-config" : "llm-profiles";
            return [new DoctorCheckResult { Check = Check, Status = StatusOk, Detail = $"skipped (the Llm section is refused — see {see})" }];
        }

        if (RunnerExecution.CheckSettingsMounts(llm.SettingsPath) is { } mounts)
            return [new DoctorCheckResult { Check = Check, Status = StatusFail, Detail = mounts }];

        var refusals = RunnerHost.ValidateSettings(llm.SettingsPath, (_, services) =>
        {
            services.AddOrkeonHumanInput();
            services.AddSemanticSearchTool();
            RunCommand.AddCliRagServices(services);
        });
        if (refusals.Count == 0)
            return [new DoctorCheckResult { Check = Check, Status = StatusOk, Detail = "the settings pass the start validation of orkeon run" }];

        return refusals.Select(refusal => new DoctorCheckResult { Check = Check, Status = StatusFail, Detail = refusal });
    }

    private static async Task<DoctorCheckResult> CheckLlmReachabilityAsync(LlmContext llm, CancellationToken ct)
    {
        const string Check = "llm-reachability";

        if (!llm.HasLlmSection)
            return new DoctorCheckResult { Check = Check, Status = StatusOk, Detail = "skipped (no Llm section configured)" };

        if (llm.SettingsError is not null)
            return new DoctorCheckResult { Check = Check, Status = StatusOk, Detail = "skipped (the Llm section cannot be read — see llm-config)" };

        var catalogKey = llm.ProviderTypeName is not null
            ? CatalogKeyByProviderType.GetValueOrDefault(llm.ProviderTypeName)
            : null;

        var baseUrl = llm.BaseUrl;
        if (baseUrl is null && catalogKey is not null)
        {
            try { baseUrl = LlmCatalogClient.ResolveBaseUrl(catalogKey, null); }
            catch (NotSupportedException) { /* no default endpoint either */ }
        }

        if (baseUrl is null)
            return new DoctorCheckResult { Check = Check, Status = StatusWarn, Detail = "skipped (no base URL to probe)" };

        // 1. Cheap TCP probe — an active refusal is the conclusive "nothing is listening".
        var tcpReachable = await RunnerExecution
            .IsLlmEndpointReachableAsync(baseUrl, TcpProbeTimeout, ct).ConfigureAwait(false);
        if (!tcpReachable)
        {
            return new DoctorCheckResult
            {
                Check = Check,
                Status = StatusFail,
                Detail = $"endpoint {baseUrl} refused the connection — is the server running?",
            };
        }

        // 2. Minimal real request: the provider's model catalogue (never the full protocol).
        if (catalogKey is null)
            return new DoctorCheckResult { Check = Check, Status = StatusOk, Detail = $"endpoint {baseUrl} accepts TCP connections (no catalogue endpoint to query)" };

        using var client = new HttpClient { Timeout = CatalogTimeout };
        try
        {
            var models = await LlmCatalogClient
                .ListAsync(client, catalogKey, baseUrl.TrimEnd('/'), llm.ApiKey, ct).ConfigureAwait(false);
            return new DoctorCheckResult
            {
                Check = Check,
                Status = StatusOk,
                Detail = $"endpoint {baseUrl} reachable — serves {models.Count} model(s)",
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex) when (
            ex.HttpRequestError is HttpRequestError.NameResolutionError
                or HttpRequestError.ConnectionError
                or HttpRequestError.SecureConnectionError)
        {
            return new DoctorCheckResult { Check = Check, Status = StatusFail, Detail = $"endpoint {baseUrl} unreachable: {ex.Message}" };
        }
        catch (HttpRequestException ex)
        {
            // The server answered — reachable, but the catalogue call was rejected (bad key, …).
            return new DoctorCheckResult { Check = Check, Status = StatusWarn, Detail = $"endpoint {baseUrl} reachable, but the catalogue request failed: {ex.Message}" };
        }
        catch (TaskCanceledException)
        {
            return new DoctorCheckResult
            {
                Check = Check,
                Status = StatusFail,
                Detail = $"endpoint {baseUrl} timed out after {CatalogTimeout.TotalSeconds:N0}s",
            };
        }
        catch (JsonException ex)
        {
            return new DoctorCheckResult { Check = Check, Status = StatusWarn, Detail = $"endpoint {baseUrl} reachable, but returned an unexpected payload: {ex.Message}" };
        }
    }

    private static async Task<DoctorCheckResult> CheckEsbuildAsync(IConfiguration configuration, CancellationToken ct)
    {
        const string Check = "esbuild";
        string binary;
        try
        {
            // The transpiler's own chain, built the way `orkeon run` builds it:
            // Orkeon:Scripting:Toolchain:EsbuildPath → ORKEON_ESBUILD_PATH → bundled → repo → PATH.
            using var transpiler = EsbuildTranspiler.Create(configuration);
            binary = transpiler.ResolveBinary();
        }
        catch (EsbuildNotFoundException)
        {
            return new DoctorCheckResult
            {
                Check = Check,
                Status = StatusWarn,
                Detail = "not found — required only for .ork.ts scripts (YAML crews are unaffected); the dotnet tool does not ship it: install with `npm install -g esbuild`, or point ORKEON_ESBUILD_PATH or Orkeon:Scripting:Toolchain:EsbuildPath at the binary; editor typings: `orkeon typings`",
            };
        }

        var version = await TryReadEsbuildVersionAsync(binary, ct).ConfigureAwait(false);
        return new DoctorCheckResult
        {
            Check = Check,
            Status = version is not null ? StatusOk : StatusWarn,
            Detail = version is not null
                ? $"v{version} at {binary} (required only for .ork.ts scripts; editor typings: `orkeon typings`)"
                : $"resolved at {binary}, but `--version` did not answer",
        };
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort version read: any failure downgrades the check detail, never crashes doctor.")]
    private static async Task<string?> TryReadEsbuildVersionAsync(string binary, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = binary,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--version");

            using var process = Process.Start(psi);
            if (process is null) return null;

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(EsbuildVersionTimeout);
                var stdout = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token).ConfigureAwait(false);
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);

                var version = stdout.Trim();
                return process.ExitCode == 0 && version.Length > 0 ? version : null;
            }
            finally
            {
                // Same discipline as EsbuildTranspiler: a timed-out/faulted probe must not
                // leave a zombie esbuild behind.
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                }
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static DoctorCheckResult CheckLocalEmbeddings()
    {
        var modelDir = Path.Combine(AppContext.BaseDirectory, "LocalEmbeddingsModel", "default");
        var present = File.Exists(Path.Combine(modelDir, "model.onnx"))
            && File.Exists(Path.Combine(modelDir, "vocab.txt"));

        return new DoctorCheckResult
        {
            Check = "local-embeddings",
            Status = present ? StatusOk : StatusWarn,
            Detail = present
                ? "BGE-micro-v2 model present (on-device semantic search available)"
                : $"model files missing under {modelDir} — semantic agent selection and codebase_search degrade to remote/none",
        };
    }

    /// <summary>
    /// The reranker weights, actually opened.
    /// <para>
    /// This used to be a hard-coded <c>ok</c> with a hard-coded detail, on the reasoning that
    /// the weights are embedded resources of a package the CLI always references — true, and
    /// still not a check: a trimmed self-contained publish, or a renamed resource, leaves the
    /// reranker broken and the doctor cheerful. Every neighbouring check probes; this one
    /// opens the stream and reports its size.
    /// </para>
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Diagnostic probe: any failure opening the embedded weights is the answer the check exists to give, whatever its type.")]
    private static DoctorCheckResult CheckOnnxReranker()
    {
        try
        {
            using var model = MsMarcoMiniLmModel.OpenModelStream();
            using var vocab = MsMarcoMiniLmModel.OpenVocabStream();

            return new DoctorCheckResult
            {
                Check = "onnx-reranker",
                Status = StatusOk,
                Detail = string.Create(
                    CultureInfo.InvariantCulture,
                    $"ms-marco-MiniLM-L-6-v2 weights readable ({model.Length / (1024 * 1024)} MB, offline)"),
            };
        }
        catch (Exception ex)
        {
            return new DoctorCheckResult
            {
                Check = "onnx-reranker",
                Status = StatusWarn,
                Detail = $"embedded weights unreadable ({ex.GetType().Name}) — the `balanced` and "
                    + "`quality` RAG profiles fall back to the LLM listwise reranker",
            };
        }
    }

    private static DoctorCheckResult CheckTreeSitterGrammars()
    {
        // Dev/test layouts keep native libraries under runtimes/{rid}/native; self-contained
        // publishes flatten them next to the executable. Probe both, tolerantly (⚠️ only).
        var prefix = NativeLibraryPrefix();
        var extension = NativeLibraryExtension();

        string[] searchDirs =
        [
            AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native"),
        ];

        var missing = TreeSitterLibraries
            .Where(lib => !searchDirs.Any(dir => File.Exists(Path.Combine(dir, prefix + lib + extension))))
            .ToList();

        return new DoctorCheckResult
        {
            Check = "tree-sitter",
            Status = missing.Count == 0 ? StatusOk : StatusWarn,
            Detail = missing.Count == 0
                ? $"{TreeSitterLibraries.Length} native libraries present (RaggableTree code analysis available)"
                : $"missing native libraries: {string.Join(", ", missing)} — RaggableTree tools degrade for those languages",
        };
    }

    private static DoctorCheckResult CheckWorkspaceWrite(string cwd)
    {
        // The runners' default /output mount is {cwd}/.orkeon — the one path every run
        // must be able to write. The only mutating check; the finally guarantees it removes
        // everything it created even when the probe fails halfway through.
        var stateDir = Path.Combine(cwd, ConventionalNames.StateDirectory);
        var existedBefore = Directory.Exists(stateDir);
        var probeFile = Path.Combine(stateDir, $".doctor-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(stateDir);
            File.WriteAllText(probeFile, "ok");

            return new DoctorCheckResult
            {
                Check = "workspace-write",
                Status = StatusOk,
                Detail = "./.orkeon is writable (run outputs and manifests will land there)",
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new DoctorCheckResult
            {
                Check = "workspace-write",
                Status = StatusFail,
                Detail = $"cannot write ./.orkeon in {cwd}: {ex.Message}",
            };
        }
        finally
        {
            try
            {
                if (File.Exists(probeFile))
                    File.Delete(probeFile);
                if (!existedBefore && Directory.Exists(stateDir))
                    Directory.Delete(stateDir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup; the check result above already tells the story.
            }
        }
    }

    // ── rendering ───────────────────────────────────────────────────────────

    private static void PrintTable(IReadOnlyList<DoctorCheckResult> results)
    {
        var width = results.Max(r => r.Check.Length);
        foreach (var result in results)
        {
            var glyph = result.Status switch
            {
                StatusOk => "✅",
                StatusWarn => "⚠️",
                _ => "❌",
            };
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{glyph} {result.Check.PadRight(width)}  {result.Detail}"));
        }
    }
}
