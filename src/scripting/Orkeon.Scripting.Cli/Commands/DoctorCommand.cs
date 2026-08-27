using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Hosting;
using Orkeon.Infrastructure.LLMs;
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
/// <see cref="RunnerExecution.IsLlmEndpointReachableAsync"/>, <see cref="EsbuildTranspiler"/>)
/// — no duplicated HTTP client and no duplicated resolution chain. The only mutating check
/// (workspace write) cleans up after itself.
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
        ["GroqLlmProvider"] = "groq",
        ["DeepSeekLlmProvider"] = "deepseek",
        ["KimiLlmProvider"] = "kimi",
        ["QwenLlmProvider"] = "qwen",
        ["MistralLlmProvider"] = "mistral",
        ["TogetherAiLlmProvider"] = "together",
        ["HuggingFaceLlmProvider"] = "huggingface",
        ["ZaiLlmProvider"] = "zai",
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
            var results = await RunChecksAsync(cwd, cts.Token).ConfigureAwait(false);

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

    private static async Task<IReadOnlyList<DoctorCheckResult>> RunChecksAsync(string cwd, CancellationToken ct)
    {
        var llm = ReadLlmContext(cwd);

        return
        [
            CheckDotnetRuntime(),
            CheckAppSettings(llm),
            CheckLlmConfig(llm),
            await CheckLlmReachabilityAsync(llm, ct).ConfigureAwait(false),
            await CheckEsbuildAsync(ct).ConfigureAwait(false),
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
        public string? ApiKey { get; init; }
        public string? ProviderTypeName { get; init; }
        public string? ProviderDisplayName { get; init; }
    }

    private static LlmContext ReadLlmContext(string cwd)
    {
        // Same chain as every runner (explicit → local → walk-up → global), quiet: doctor
        // reports the outcome itself instead of the resolver's stderr warning.
        var settingsPath = RunnerSettings.ResolveSettingsPath(null, cwd, quiet: true);

        var builder = new ConfigurationBuilder();
        if (settingsPath is not null)
            builder.AddJsonFile(settingsPath, optional: true);
        builder.AddEnvironmentVariables("ORKEON_");
        var configuration = builder.Build();

        var section = configuration.GetSection("Llm");
        if (!section.Exists())
            return new LlmContext { SettingsPath = settingsPath, HasLlmSection = false };

        var model = section["Model"];
        var baseUrl = section["BaseUrl"];
        var context = new LlmContext
        {
            SettingsPath = settingsPath,
            HasLlmSection = true,
            Model = model,
            BaseUrl = baseUrl,
            ApiKey = section["ApiKey"],
        };

        // Reuse the factory's own inference (BaseUrl → model → key) instead of duplicating it.
        try
        {
            var config = LlmConfig.Create(model ?? "gpt-4") with
            {
                BaseUrl = baseUrl is not null ? new Uri(baseUrl) : null,
#pragma warning disable CS0618 // doctor inspects the raw config, no secret store involved
                ApiKey = section["ApiKey"],
#pragma warning restore CS0618
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

    private static string ToDisplayName(string providerTypeName) =>
        providerTypeName
            .Replace("LlmProvider", "", StringComparison.Ordinal)
            .Replace("Provider", "", StringComparison.Ordinal);

    // ── individual checks ───────────────────────────────────────────────────

    private static DoctorCheckResult CheckDotnetRuntime()
    {
        // Self-contained publishes carry the host resolver next to the app; a
        // framework-dependent deployment resolves it from the shared installation.
        var hostfxr = OperatingSystem.IsWindows() ? "hostfxr.dll"
            : OperatingSystem.IsMacOS() ? "libhostfxr.dylib"
            : "libhostfxr.so";
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

        var provider = llm.ProviderDisplayName ?? "unrecognised";
        return new DoctorCheckResult
        {
            Check = "llm-config",
            Status = llm.ProviderDisplayName is not null ? StatusOk : StatusWarn,
            Detail = $"provider {provider}, model {llm.Model ?? "(default)"}, endpoint {llm.BaseUrl ?? "(provider default)"}",
        };
    }

    private static async Task<DoctorCheckResult> CheckLlmReachabilityAsync(LlmContext llm, CancellationToken ct)
    {
        const string Check = "llm-reachability";

        if (!llm.HasLlmSection)
            return new DoctorCheckResult { Check = Check, Status = StatusOk, Detail = "skipped (no Llm section configured)" };

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

    private static async Task<DoctorCheckResult> CheckEsbuildAsync(CancellationToken ct)
    {
        const string Check = "esbuild";
        string binary;
        try
        {
            // The transpiler's own chain: config → ORKEON_ESBUILD_PATH → bundled → repo → PATH.
            using var transpiler = new EsbuildTranspiler();
            binary = transpiler.ResolveBinary();
        }
        catch (EsbuildNotFoundException)
        {
            return new DoctorCheckResult
            {
                Check = Check,
                Status = StatusWarn,
                Detail = "not found — required only for .ork.ts scripts (YAML crews are unaffected); install with `npm install -g esbuild` or set ORKEON_ESBUILD_PATH",
            };
        }

        var version = await TryReadEsbuildVersionAsync(binary, ct).ConfigureAwait(false);
        return new DoctorCheckResult
        {
            Check = Check,
            Status = version is not null ? StatusOk : StatusWarn,
            Detail = version is not null
                ? $"v{version} at {binary} (required only for .ork.ts scripts)"
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
        var prefix = OperatingSystem.IsWindows() ? "" : "lib";
        var extension = OperatingSystem.IsWindows() ? ".dll"
            : OperatingSystem.IsMacOS() ? ".dylib"
            : ".so";

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
        var stateDir = Path.Combine(cwd, ".orkeon");
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
