using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>What one sandboxed run produced, as the diagnosis will see it.</summary>
internal sealed record ForgeTestRun
{
    /// <summary>1-based run number — the cycle's iteration.</summary>
    [JsonPropertyName("run")]
    public int Run { get; init; }

    /// <summary>Whether the crew reached its end.</summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>The crew's aggregated output.</summary>
    [JsonPropertyName("output")]
    public string Output { get; init; } = "";

    /// <summary>Tasks that completed.</summary>
    [JsonPropertyName("taskCount")]
    public int TaskCount { get; init; }

    /// <summary>Wall time of the run, milliseconds.</summary>
    [JsonPropertyName("durationMs")]
    public long DurationMs { get; init; }

    /// <summary>Tokens the run consumed, when the providers reported them.</summary>
    [JsonPropertyName("tokens")]
    public long? Tokens { get; init; }

    /// <summary>
    /// The ascending half of <see cref="Tokens"/> — everything the crew sent to its
    /// models. Null when the run measured nothing; never a fabricated zero.
    /// </summary>
    [JsonPropertyName("promptTokens")]
    public long? PromptTokens { get; init; }

    /// <summary>The descending half — everything the models sent back; null when unmeasured.</summary>
    [JsonPropertyName("completionTokens")]
    public long? CompletionTokens { get; init; }

    /// <summary>
    /// Prompt tokens served from the provider's cache — a partition of the prompt side,
    /// never additive to <see cref="Tokens"/>; null when unmeasured (W-08).
    /// </summary>
    [JsonPropertyName("cacheHitTokens")]
    public long? CacheHitTokens { get; init; }

    /// <summary>Prompt tokens the provider computed; null when unmeasured (W-08).</summary>
    [JsonPropertyName("cacheMissTokens")]
    public long? CacheMissTokens { get; init; }

    /// <summary>Why the run failed, when it did.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

/// <summary>
/// Executes the rendered crew once (SPEC-ORKEON-FORGE §9). One seam, two implementations:
/// the production bench below over the engine host, and the tests' scripted one — no stage
/// knows the difference.
/// </summary>
internal interface IForgeTestBench
{
    /// <summary>Runs the session's rendered crew on the brief's sample input.</summary>
    Task<ForgeTestRun> ExecuteAsync(ForgeSession session, int runNumber, CancellationToken cancellationToken);
}

/// <summary>
/// The production bench: loads the crew **from the rendered files** — what was written is
/// what runs, the same artifact `orkeon run` will load after promotion — then kicks it off
/// in-process. <c>KickoffAsync</c> never throws (its fault barrier): failure is detected on
/// the shape of the result, exactly as SPEC §9.3 prescribes.
/// </summary>
internal sealed class ForgeCrewTestBench : IForgeTestBench
{
    /// <summary>The failure prefix of the orchestrator's fault barrier.</summary>
    public const string FailurePrefix = "Crew execution failed:";

    private readonly IServiceProvider _services;
    private readonly string _crewVirtualPath;
    private readonly Func<Orkeon.Scripting.Toolchain.IScriptTranspiler> _transpilerFactory;

    /// <summary>
    /// Builds the bench over the engine host's services. <paramref name="transpilerFactory"/>
    /// serves the script format only; the default is the real esbuild chain — tests inject
    /// a pass-through, production never does.
    /// </summary>
    public ForgeCrewTestBench(
        IServiceProvider services,
        string crewVirtualPath = "/forge/crew",
        Func<Orkeon.Scripting.Toolchain.IScriptTranspiler>? transpilerFactory = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _crewVirtualPath = crewVirtualPath;
        _transpilerFactory = transpilerFactory ?? (static () => new Orkeon.Scripting.Toolchain.EsbuildTranspiler());
    }

    /// <inheritdoc />
    public async Task<ForgeTestRun> ExecuteAsync(
        ForgeSession session, int runNumber, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var factory = _services.GetRequiredService<ICrewFactory>();
        var orchestration = _services.GetRequiredService<ICrewOrchestrationService>();

        // Both formats load FROM THE RENDERED FILE — what was written is what runs — and
        // converge on the same CrewConfiguration, the same factory, the same kickoff
        // (SPEC §8.3's single pipeline, extended to execution).
        var configuration = ForgeSession.IsScriptFormat(session.Document.Format)
            ? await LoadScriptConfigurationAsync(session, cancellationToken).ConfigureAwait(false)
            : await _services.GetRequiredService<ICrewDefinitionLoader>()
                .LoadFromDirectoryAsync(_crewVirtualPath, cancellationToken).ConfigureAwait(false);
        var crew = await factory.CreateFromConfigAsync(configuration, cancellationToken).ConfigureAwait(false);

        var brief = session.TryLoadArtifact<ForgeBrief>(ForgeSession.BriefFileName);
        var input = CrewInput.WithStringVariables(
            brief?.Sample?.InitialContext,
            brief?.Sample?.Variables ?? new Dictionary<string, string>());

        var output = await orchestration.KickoffAsync(crew.Id, input, cancellationToken).ConfigureAwait(false);

        var failed = output.TaskOutputs.Count == 0
            || output.FinalOutput.StartsWith(FailurePrefix, StringComparison.Ordinal);

        return new ForgeTestRun
        {
            Run = runNumber,
            Success = !failed,
            Output = output.FinalOutput,
            TaskCount = output.TaskOutputs.Count,
            DurationMs = (long)output.Duration.TotalMilliseconds,
            Tokens = output.TokensUsed?.TotalTokens,
            // The split was measured all along and thrown away here: the crew's own
            // TokenUsage carries both halves, and only the sum reached the protocol.
            PromptTokens = output.TokensUsed?.PromptTokens,
            CompletionTokens = output.TokensUsed?.CompletionTokens,
            CacheHitTokens = output.TokensUsed?.CacheHitTokens,
            CacheMissTokens = output.TokensUsed?.CacheMissTokens,
            Error = failed ? output.FinalOutput : null,
        };
    }

    /// <summary>
    /// Loads the rendered <c>crew.ork.ts</c> the way <c>orkeon run</c> does: ScriptHost →
    /// <c>globalThis.crew</c> → <c>JsCrewConfigurationAdapter</c> — the same
    /// CrewConfiguration surface the YAML loader produces.
    /// </summary>
    private async Task<Orkeon.Domain.Configuration.CrewConfiguration> LoadScriptConfigurationAsync(
        ForgeSession session, CancellationToken cancellationToken)
    {
        var loggerFactory = _services.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>();
        var engineFactory = new Orkeon.Scripting.JsEngineFactory(
            loggerFactory: loggerFactory,
            configuration: _services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>(),
            builtInTools: _services.GetServices<Orkeon.Domain.Tools.IBaseTool>().ToList(),
            llmProvider: _services.GetService<Orkeon.Domain.SharedKernel.ILlmProvider>(),
            permissionGate: _services.GetService<Orkeon.Application.Interfaces.Security.IPermissionGate>(),
            deltaSink: _services.GetService<Orkeon.Application.Interfaces.Ports.ILlmDeltaSink>(),
            usageSink: _services.GetService<Orkeon.Application.Interfaces.Ports.ILlmUsageSink>());

        var transpiler = _transpilerFactory();
        try
        {
            var scriptHost = new Orkeon.Scripting.ScriptHost(
                _services.GetRequiredService<Orkeon.Domain.FileSystem.IFileSystemService>(),
                transpiler,
                engineFactory,
                Microsoft.Extensions.Logging.LoggerFactoryExtensions.CreateLogger<Orkeon.Scripting.ScriptHost>(loggerFactory));

            var physicalPath = Path.Combine(
                session.Directory, ForgeYamlRenderer.CrewDirectoryName, ForgeScriptRenderer.ScriptFileName);
            var virtualPath = $"{_crewVirtualPath}/{ForgeScriptRenderer.ScriptFileName}";

            var jsCrew = await scriptHost.LoadCrewFromFileAsync(physicalPath, virtualPath, cancellationToken)
                .ConfigureAwait(false);
            return Orkeon.Scripting.Adapters.JsCrewConfigurationAdapter.ToConfiguration(jsCrew);
        }
        finally
        {
            (transpiler as IDisposable)?.Dispose();
        }
    }
}
