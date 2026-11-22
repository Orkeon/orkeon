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

    /// <summary>The mail actions the try simulated.</summary>
    [JsonPropertyName("mailActions")]
    public System.Collections.Immutable.ImmutableList<ForgeMailAction> MailActions { get; init; } = [];

    /// <summary>Why the trial mailbox holds the sample alone, when drawing failed.</summary>
    [JsonPropertyName("mailSeedNote")]
    public string MailSeedNote { get; init; } = "";

    /// <summary>The decision circuit of a single-pass trial (<c>on</c>, <c>off</c>); null when the host's was not known.</summary>
    [JsonPropertyName("circuit")]
    public string? Circuit { get; init; }

    /// <summary>
    /// The passes of the trial, in the order they ran (#431): one, or two for an <c>optional</c> crew (on, then off).
    /// The top-level figures are their sums; <see cref="Success"/> holds when every pass succeeded.
    /// </summary>
    [JsonPropertyName("passes")]
    public IReadOnlyList<ForgeTestPass>? Passes { get; init; }
}

/// <summary>One pass of a trial (#431): the crew run once, under one decision circuit.</summary>
internal sealed record ForgeTestPass
{
    /// <summary><c>on</c> or <c>off</c>; null when the host's circuit was not known.</summary>
    [JsonPropertyName("circuit")]
    public string? Circuit { get; init; }

    /// <summary>Why the circuit was off (<c>switch</c>, <c>settings</c>, <c>no-provider</c>, <c>trial</c>); null when on.</summary>
    [JsonPropertyName("cause")]
    public string? Cause { get; init; }

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>The pass's <c>output.md</c>, relative to the session (<c>output</c> on the wire, as #431 names it).</summary>
    [JsonPropertyName("output")]
    public string? OutputPath { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; init; }

    [JsonPropertyName("tokens")]
    public long? Tokens { get; init; }

    [JsonPropertyName("promptTokens")]
    public long? PromptTokens { get; init; }

    [JsonPropertyName("completionTokens")]
    public long? CompletionTokens { get; init; }

    [JsonPropertyName("cacheHitTokens")]
    public long? CacheHitTokens { get; init; }

    [JsonPropertyName("cacheMissTokens")]
    public long? CacheMissTokens { get; init; }

    [JsonPropertyName("mailActions")]
    public System.Collections.Immutable.ImmutableList<ForgeMailAction> MailActions { get; init; } = [];
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

    /// <summary>
    /// Runs the crew once under <paramref name="circuit"/> (#431), the host's when null: the trial of an
    /// <c>optional</c> crew runs it twice, on then off. A bench that knows no circuit runs the host's.
    /// </summary>
    Task<ForgeTestRun> ExecuteAsync(
        ForgeSession session, int runNumber, Orkeon.Application.Crew.Execution.DecisionCircuit? circuit, CancellationToken cancellationToken) =>
        ExecuteAsync(session, runNumber, cancellationToken);
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
    private readonly string? _workspace;
    private readonly string? _readRoot;
    private readonly ForgeTrialMailAuthor? _mailAuthor;

    /// <summary>
    /// Builds the bench over the engine host's services. <paramref name="transpilerFactory"/>
    /// serves the script format only; the default is the real esbuild chain, configured by the
    /// engine host's <c>Orkeon:Scripting:Toolchain</c> section — tests inject a pass-through,
    /// production never does.
    /// </summary>
    /// <para>
    /// <paramref name="workspace"/> and <paramref name="readRoot"/> (<c>--read</c>) say where an
    /// unbound input folder reads (<see cref="ForgeFolders.TrialMounts"/>); without a workspace
    /// the run keeps the host's mounts as they are.
    /// </para>
    public ForgeCrewTestBench(
        IServiceProvider services,
        string crewVirtualPath = "/forge/crew",
        Func<Orkeon.Scripting.Toolchain.IScriptTranspiler>? transpilerFactory = null,
        string? workspace = null,
        string? readRoot = null,
        ForgeTrialMailAuthor? mailAuthor = null)
    {
        _mailAuthor = mailAuthor;
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _crewVirtualPath = crewVirtualPath;
        _workspace = workspace;
        _readRoot = readRoot;
        _transpilerFactory = transpilerFactory ?? (() => Orkeon.Scripting.Toolchain.EsbuildTranspiler.Create(
            services.GetService<Microsoft.Extensions.Configuration.IConfiguration>()));
    }

    /// <inheritdoc />
    public Task<ForgeTestRun> ExecuteAsync(ForgeSession session, int runNumber, CancellationToken cancellationToken) =>
        ExecuteAsync(session, runNumber, circuit: null, cancellationToken);

    /// <inheritdoc />
    public async Task<ForgeTestRun> ExecuteAsync(
        ForgeSession session, int runNumber, Orkeon.Application.Crew.Execution.DecisionCircuit? circuit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        // The trial sees the confirmed folders and nothing else of the team's: one
        // mount set for this run, entered for its whole flow and left with it.
        using var folders = _workspace is null ? null : ForgeTrialScope.Enter(_services, session, _workspace, _readRoot);

        // A trial is a run: it loads and kicks off in one scope of its own, as `orkeon run`
        // and orkeon-host do (GAP-25). The factory, the orchestrator and the repositories the
        // crew is loaded into are scoped; resolved from the engine host's root they kept every
        // trial's crew for the life of the session, and a host validating scopes refused them.
        var scope = _services.CreateAsyncScope();
        await using var _ = scope.ConfigureAwait(false);
        // The pass's circuit (#431), set before the factory and the orchestrator are resolved: they read it.
        if (circuit is not null && scope.ServiceProvider.GetService<Orkeon.Application.Crew.Execution.DecisionCircuitOverride>() is { } scoped)
            scoped.Circuit = circuit;
        var factory = scope.ServiceProvider.GetRequiredService<ICrewFactory>();
        var orchestration = scope.ServiceProvider.GetRequiredService<ICrewOrchestrationService>();

        // Both formats load FROM THE RENDERED FILE — what was written is what runs — and
        // converge on the same CrewConfiguration, the same factory, the same kickoff
        // (SPEC §8.3's single pipeline, extended to execution).
        var configuration = ForgeSession.IsScriptFormat(session.Document.Format)
            ? await LoadScriptConfigurationAsync(session, scope.ServiceProvider, cancellationToken).ConfigureAwait(false)
            : await _services.GetRequiredService<ICrewDefinitionLoader>()
                .LoadFromDirectoryAsync(_crewVirtualPath, cancellationToken).ConfigureAwait(false);

        // A trial runs without memory (GAP-30): under the name of the crew being designed, it would
        // store trial outputs that the promoted crew would then recall as its earlier runs. The
        // team's memory starts with its first real run.
        var crew = await factory.CreateFromConfigAsync(
            configuration with { Memory = false, MemoryProvider = null }, cancellationToken).ConfigureAwait(false);

        var brief = session.TryLoadArtifact<ForgeBrief>(ForgeSession.BriefFileName);
        var input = CrewInput.WithStringVariables(
            brief?.Sample?.InitialContext,
            brief?.Sample?.Variables ?? new Dictionary<string, string>());

        var crewTools = configuration.Agents.SelectMany(a => a.Tools)
            .Concat(configuration.Tasks.SelectMany(t => t.Tools)).ToList();
        var mailbox = _mailAuthor is null
            ? new ForgeTrialMailbox(Orkeon.Tools.Email.Trial.TrialMailboxSeed.Sample(), "")
            : await _mailAuthor.SeedAsync(session, brief, crewTools, cancellationToken).ConfigureAwait(false);

        Orkeon.Application.Interfaces.Services.CrewOutput output;
        System.Collections.Generic.IReadOnlyList<Orkeon.Tools.Email.Trial.SimulatedMailAction> actions;
        using (var trial = Orkeon.Tools.Email.Trial.EmailTrial.Enter(mailbox.Seed))
        {
            output = await orchestration.KickoffAsync(crew.Id, input, cancellationToken).ConfigureAwait(false);
            actions = trial.Actions;
        }

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
            MailActions = [.. actions.Select(ToMailAction)],
            MailSeedNote = mailbox.Note,
        };
    }

    private static ForgeMailAction ToMailAction(Orkeon.Tools.Email.Trial.SimulatedMailAction action) => new()
    {
        Kind = action.Kind switch
        {
            Orkeon.Tools.Email.Trial.SimulatedMailActionKind.CreateFolder => "create_folder",
            Orkeon.Tools.Email.Trial.SimulatedMailActionKind.RenameFolder => "rename_folder",
            Orkeon.Tools.Email.Trial.SimulatedMailActionKind.SaveAttachment => "save_attachment",
            Orkeon.Tools.Email.Trial.SimulatedMailActionKind.Send => "send",
            Orkeon.Tools.Email.Trial.SimulatedMailActionKind.Draft => "draft",
            Orkeon.Tools.Email.Trial.SimulatedMailActionKind.Move => "move",
            Orkeon.Tools.Email.Trial.SimulatedMailActionKind.Mark => "mark",
            Orkeon.Tools.Email.Trial.SimulatedMailActionKind.Delete => "delete",
            var other => throw new ArgumentOutOfRangeException(nameof(action), other, null),
        },
        Account = action.Account,
        Recipients = action.Recipients,
        Subject = action.Subject,
        Body = action.Body,
        Folder = action.Folder,
        Target = action.Target,
    };

    /// <summary>
    /// Loads the rendered <c>crew.ork.ts</c> the way <c>orkeon run</c> does: ScriptHost →
    /// <c>globalThis.crew</c> → <c>JsCrewConfigurationAdapter</c> — the same
    /// CrewConfiguration surface the YAML loader produces — with the same host ports, the host's
    /// LLM profiles included: a crew naming one (<c>llm.profile(name)</c>) failed its trial with
    /// "Known profiles: default." and ran once promoted (GAP-27). The scoped ports - System One's pick (#460) and the
    /// pruning an act's <c>prune</c> asks for (#461) - come from <paramref name="trialServices"/>, the trial's scope.
    /// </summary>
    private async Task<Orkeon.Domain.Configuration.CrewConfiguration> LoadScriptConfigurationAsync(
        ForgeSession session, IServiceProvider trialServices, CancellationToken cancellationToken)
    {
        var loggerFactory = _services.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>();
        var engineFactory = new Orkeon.Scripting.JsEngineFactory(
            loggerFactory: loggerFactory,
            builtInTools: _services.GetServices<Orkeon.Domain.Tools.IBaseTool>().OfType<Orkeon.Domain.Tools.IBaseTool>().ToList(),
            llmProvider: _services.GetService<Orkeon.Domain.SharedKernel.ILlmProvider>(),
            hostPorts: new Orkeon.Scripting.ScriptingHostPorts
            {
                PermissionGate = _services.GetService<Orkeon.Application.Interfaces.Security.IPermissionGate>(),
                DeltaSink = _services.GetService<Orkeon.Application.Interfaces.Ports.ILlmDeltaSink>(),
                ToolCallDlp = Orkeon.Application.Crew.ToolCallDeltaDlp.From(_services),
                ToolInvocation = _services.GetService<Orkeon.Application.Interfaces.Security.IToolInvocationPipeline>(),
                LlmProfiles = _services.GetService<Orkeon.Application.Interfaces.Ports.ILlmProfileRegistry>(),
                // The trial's mounts (it follows the ambient scope Enter opens): a body's working root is
                // checked against them and its ctx.fileSystem.prompt names them, as under orkeon run (#378).
                FileSystem = _services.GetService<Orkeon.Domain.FileSystem.IFileSystemService>(),
                // ctx.llm.pick and act's prune, as under orkeon run: the trial's scope, its circuit and its observers.
                SystemOnePicker = trialServices.GetService<Orkeon.Application.Crew.Routing.ISystemOnePicker>(),
                ConversationPruner = trialServices.GetService<Orkeon.Application.Crew.Compaction.IConversationPruner>(),
            });

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

/// <summary>
/// The trial's mount set: the session's folders at the places
/// <see cref="ForgeFolders.TrialMounts"/> gives them, every other mount of the host carried
/// forward — <c>/forge</c>, the settings' own roots, the internal ones — except the two canonical
/// roots the forge mounts for its assistant, which the team only sees when its list holds them.
/// </summary>
internal static class ForgeTrialScope
{
    /// <summary>The roots the forge host always mounts, and the trial drops unless its list claims them.</summary>
    private static readonly string[] CanonicalRoots =
        [Orkeon.Constants.FileSystem.RunnerVirtualRoots.Workspace, Orkeon.Constants.FileSystem.RunnerVirtualRoots.Output];

    /// <summary>
    /// The registry the trial enters: the folder mounts first, then the host's mounts no folder
    /// claims. Exposed apart from <see cref="Enter"/> so it can be asserted without a host.
    /// </summary>
    public static Orkeon.Domain.FileSystem.FileSystemRegistry Compose(
        Orkeon.Domain.FileSystem.FileSystemRegistry boot, IReadOnlyList<ForgeTrialMount> mounts)
    {
        ArgumentNullException.ThrowIfNull(boot);
        ArgumentNullException.ThrowIfNull(mounts);

        var folders = mounts.Select(m => Orkeon.Domain.FileSystem.FileSystemMount.Parse(m.Spec)).ToList();
        var claimed = folders.Select(m => m.VirtualPath).ToHashSet(StringComparer.Ordinal);
        var carried = boot.GetMounts()
            .Where(m => !claimed.Contains(m.VirtualPath) && !CanonicalRoots.Contains(m.VirtualPath, StringComparer.Ordinal));
        var registry = new Orkeon.Domain.FileSystem.FileSystemRegistry([.. folders, .. carried]);

        // The trial keeps the host's working and write directories when it still mounts them,
        // else takes its own (/workspace, else /output): without them a relative path and a
        // shell command without working_directory had nowhere to go in a trial.
        Orkeon.Infrastructure.FileSystem.WorkingDirectoryDesignation.CarryForward(boot, registry);
        return registry;
    }

    /// <summary>
    /// Enters the trial's mount set for the current flow; null when the host has no ambient
    /// scope to enter. Disposing the lease leaves it.
    /// </summary>
    public static IDisposable? Enter(IServiceProvider services, ForgeSession session, string workspace, string? readRoot)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(session);

        var scope = services.GetService<Orkeon.Domain.FileSystem.IFileSystemScope>();
        var boot = services.GetService<Orkeon.Domain.FileSystem.FileSystemRegistry>();
        if (scope is null || boot is null)
            return null;

        var mounts = ForgeFolders.TrialMounts(session, ForgeFolders.Of(session), workspace, readRoot);
        var registry = Compose(boot, mounts);
        return new Lease(registry, scope.Enter(registry));
    }

    /// <summary>Leaves the scope first, then disposes the registry it pointed at.</summary>
    private sealed class Lease(Orkeon.Domain.FileSystem.FileSystemRegistry registry, IDisposable token) : IDisposable
    {
        public void Dispose()
        {
            token.Dispose();
            registry.Dispose();
        }
    }
}
