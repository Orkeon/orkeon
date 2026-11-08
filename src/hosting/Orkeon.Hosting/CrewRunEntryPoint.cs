using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Infrastructure.LLMs;

namespace Orkeon.Hosting;

/// <summary>How a <see cref="CrewRunEntryPoint"/> run ended.</summary>
internal enum CrewRunOutcome
{
    /// <summary>The crew loaded and was kicked off; <see cref="CrewRunResult.Output"/> holds its output, succeeded or not.</summary>
    Completed,

    /// <summary>The crew did not load; <see cref="CrewRunResult.LoadError"/> says why.</summary>
    LoadFailed,

    /// <summary>The caller's <c>beforeKickoff</c> returned no input: nothing ran.</summary>
    Aborted,

    /// <summary>The crew was refused before it ran; <see cref="CrewRunResult.Refusal"/> says why.</summary>
    Refused,
}

/// <summary>What <see cref="CrewRunEntryPoint.RunAsync"/> hands back.</summary>
/// <param name="Outcome">How the run ended.</param>
/// <param name="Output">The crew's output; set when <paramref name="Outcome"/> is <see cref="CrewRunOutcome.Completed"/>.</param>
/// <param name="LoadError">The load failure; set when <paramref name="Outcome"/> is <see cref="CrewRunOutcome.LoadFailed"/>.</param>
/// <param name="Refusal">Why the crew was refused; set when <paramref name="Outcome"/> is <see cref="CrewRunOutcome.Refused"/>.</param>
internal sealed record CrewRunResult(CrewRunOutcome Outcome, CrewOutput? Output, Exception? LoadError, string? Refusal = null);

/// <summary>
/// The one place that loads a crew and kicks it off, in a scope of its own, and hands the output
/// back to the caller: the sequence <c>orkeon run</c> prints from (<see cref="RunnerExecution.RunOneShotAsync"/>)
/// and <c>orkeon eval run --crew</c> reads, once per dataset case, over one host (FWK-09). The host
/// is built once; every <see cref="RunAsync"/> opens its own scope, which is released when the run ends.
/// </summary>
internal sealed class CrewRunEntryPoint : IDisposable
{
    private readonly RunnerExecution.HostBootstrap _bootstrap;
    private readonly bool _ownsHost;
    private bool _mcpConnected;

    internal CrewRunEntryPoint(RunnerExecution.HostBootstrap bootstrap, bool ownsHost)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);
        _bootstrap = bootstrap;
        _ownsHost = ownsHost;
    }

    /// <summary>The host the runs share.</summary>
    internal IHost Host => _bootstrap.Host;

    /// <summary>
    /// Builds the host of the crew <paramref name="opts"/> names, with every guard of <c>orkeon run</c>.
    /// The entry point owns the host and disposes it.
    /// </summary>
    /// <param name="opts">The options naming the crew and the host's guards.</param>
    /// <param name="loggerCategory">The category of the host's logger.</param>
    /// <param name="configureServices">Services added to the host, or null.</param>
    /// <param name="entry">The entry point when the host was built, else null.</param>
    /// <param name="errorCode">The exit code of the refusal.</param>
    /// <param name="configureLogging">Replaces the verbosity-driven logging: a server whose stdout is a protocol channel sends every log to stderr.</param>
    /// <returns>False when a guard refused the run (it has already said why on the console); <paramref name="errorCode"/> is then the exit code.</returns>
    internal static bool TryOpen(
        RunnerOptionsBase opts,
        string loggerCategory,
        Action<HostBuilderContext, IServiceCollection>? configureServices,
        out CrewRunEntryPoint? entry,
        out int errorCode,
        Action<HostBuilderContext, Microsoft.Extensions.Logging.ILoggingBuilder>? configureLogging = null)
    {
        entry = null;
        if (!RunnerExecution.TryBuildHost(opts, loggerCategory, configureServices, out var bootstrap, out errorCode, configureLogging))
            return false;

        entry = new CrewRunEntryPoint(bootstrap!, ownsHost: true);
        return true;
    }

    /// <summary>
    /// Loads the crew and runs it once, in a scope of this run.
    /// </summary>
    /// <param name="beforeKickoff">
    /// Called with the loaded crew once it has loaded and before it runs: returns the input of the run,
    /// or null to stop (nothing runs, the outcome is <see cref="CrewRunOutcome.Aborted"/>).
    /// </param>
    /// <param name="resume">The checkpointed run to resume, or null.</param>
    /// <param name="ct">Cancellation: an <see cref="OperationCanceledException"/> propagates.</param>
    /// <param name="resumeInput">The answer to the question the resumed run stopped on (FWK-23), or null.</param>
    /// <param name="offline">
    /// Refuse the crew (<see cref="CrewRunOutcome.Refused"/>) when any agent or its manager would answer through a provider
    /// other than the mock: nothing runs, and the refusal names the agent and its profile.
    /// </param>
    internal async Task<CrewRunResult> RunAsync(
        Func<Domain.Crew.Crew, CancellationToken, Task<CrewInput?>> beforeKickoff,
        string? resume,
        CancellationToken ct,
        bool offline = false,
        string? resumeInput = null)
    {
        ArgumentNullException.ThrowIfNull(beforeKickoff);
        var host = _bootstrap.Host;
        var scope = host.Services.CreateAsyncScope();
        await using var _ = scope.ConfigureAwait(false);
        var (crew, loadError) = await LoadAsync(scope, ct).ConfigureAwait(false);
        if (crew is null)
            return new CrewRunResult(CrewRunOutcome.LoadFailed, null, loadError);

        if (offline && await OfflineRefusalAsync(scope.ServiceProvider, crew, ct).ConfigureAwait(false) is { } refusal)
            return new CrewRunResult(CrewRunOutcome.Refused, null, null, refusal);

        var input = await beforeKickoff(crew, ct).ConfigureAwait(false);
        if (input is null)
            return new CrewRunResult(CrewRunOutcome.Aborted, null, null);

        var orchestrator = scope.ServiceProvider.GetRequiredService<ICrewOrchestrationService>();
        var output = await orchestrator.KickoffAsync(
            crew.Id,
            input,
            RunnerRunIdentity.For(scope.ServiceProvider, crew.Name, _bootstrap.VirtualConfigPath, _bootstrap.ConfigPath, resume, resumeInput),
            ct).ConfigureAwait(false);
        return new CrewRunResult(CrewRunOutcome.Completed, output, null);
    }

    /// <summary>
    /// Loads the crew in a scope of its own and hands <paramref name="inspect"/> what the scope holds, without running it:
    /// what a server reads to describe the crew (its tasks, its placeholders) before any call. It loads the crew
    /// the way <see cref="RunAsync"/> does, so what it describes is what runs.
    /// </summary>
    /// <typeparam name="T">What <paramref name="inspect"/> returns.</typeparam>
    /// <param name="inspect">Called with the loaded crew and the services of the scope it was loaded in.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The inspection, or the load failure.</returns>
    internal async Task<(T? Value, Exception? LoadError)> InspectAsync<T>(
        Func<Domain.Crew.Crew, IServiceProvider, CancellationToken, Task<T>> inspect, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(inspect);
        var scope = _bootstrap.Host.Services.CreateAsyncScope();
        await using var _ = scope.ConfigureAwait(false);
        var (crew, loadError) = await LoadAsync(scope, ct).ConfigureAwait(false);
        if (crew is null)
            return (default, loadError);

        return (await inspect(crew, scope.ServiceProvider, ct).ConfigureAwait(false), null);
    }

    /// <summary>The MCP servers of the settings, connected once per host, then the crew loaded in <paramref name="scope"/>.</summary>
    private async Task<(Domain.Crew.Crew? Crew, Exception? LoadError)> LoadAsync(AsyncServiceScope scope, CancellationToken ct)
    {
        var host = _bootstrap.Host;

        // The MCP tools must be in the registry before the crew resolves its tools (STUDIO-21): once
        // per host, however many runs it serves.
        if (!_mcpConnected)
        {
            await McpStartup.ConnectConfiguredServersAsync(host, ct).ConfigureAwait(false);
            _mcpConnected = true;
        }

        // One scope for the load and the kickoff, as orkeon-host's CrewRunner opens per run (GAP-25):
        // the crew factory, the orchestrator and the repositories the crew is loaded into are scoped.
        var factory = scope.ServiceProvider.GetRequiredService<ICrewFactory>();
        try
        {
            var crew = await RunnerExecution.LoadCrewAsync(
                host, factory, scope.ServiceProvider, _bootstrap.VirtualConfigPath, _bootstrap.Logger, ct, _bootstrap.IsCrewDirectory)
                .ConfigureAwait(false);
            return (crew, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !RunnerExecution.IsConnectionRefused(ex))
        {
            // A crew that does not load is a configuration mistake the caller reports. A scripted
            // crew can reach the LLM while loading, though: that failure is not one, and propagates.
            return (null, ex);
        }
    }

    /// <summary>
    /// What an offline run refuses: an agent (or the manager) whose model is not the mock provider, whether it
    /// answers through its own profile, its own provider or the host default. Null when every one is on the mock.
    /// </summary>
    private static async Task<string?> OfflineRefusalAsync(IServiceProvider services, Domain.Crew.Crew crew, CancellationToken ct)
    {
        var registry = services.GetRequiredService<ILlmProfileRegistry>();
        var agents = await services.GetRequiredService<IAgentRepository>().GetByIdsAsync(crew.Agents, ct).ConfigureAwait(false);
        var offenders = new List<string>();
        foreach (var agent in agents)
        {
            var profile = agent.LlmConfig?.Profile;
            var named = string.IsNullOrWhiteSpace(profile) || LlmProfiles.IsDefault(profile) ? LlmProfiles.Default : profile.Trim();
            var provider = agent.Llm ?? registry.Resolve(named).Provider;
            if (!IsMock(provider))
                offenders.Add($"agent '{agent.Role.Value}' (profile '{(agent.Llm is null ? named : "its own provider")}', provider '{MeteredLlmProvider.Unwrap(provider).Name}')");
        }

        if (crew.ManagerLlm is { } manager && !IsMock(manager))
            offenders.Add($"the manager (provider '{MeteredLlmProvider.Unwrap(manager).Name}')");

        return offenders.Count == 0
            ? null
            : "an offline run needs every model on the mock provider, and " + string.Join("; ", offenders)
                + " would call a real one: point the profile at the mock (--llm-mock <file> turns every profile into the mock) or drop --offline.";
    }

    private static bool IsMock(Orkeon.Domain.SharedKernel.ILlmProvider provider) =>
        string.Equals(MeteredLlmProvider.Unwrap(provider).Name, Orkeon.Constants.Llm.LlmProviderKeys.Mock, StringComparison.OrdinalIgnoreCase);

    /// <summary>Disposes the host, when this entry point built it.</summary>
    public void Dispose()
    {
        if (_ownsHost)
            _bootstrap.Host.Dispose();
    }
}
