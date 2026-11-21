using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Configuration;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.SharedKernel;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.LLMs.Decision.Stages;

namespace Orkeon.Infrastructure.LLMs.Decision;

/// <summary>Registers the decision profiles of <c>Orkeon:Decision:Profiles</c>.</summary>
public static class DecisionProfileServiceCollectionExtensions
{
    /// <summary>The <c>Kind</c> of a route stage.</summary>
    public const string RouteKind = "route";

    /// <summary>The <c>Kind</c> of a guard stage.</summary>
    public const string GuardKind = "guard";

    /// <summary>The <c>Kind</c> of a verify stage.</summary>
    public const string VerifyKind = "verify";

    /// <summary>
    /// Registers each decision profile as an LLM profile a crew can name. The section is read and
    /// checked now: an unknown stage kind, a missing target or route, a threshold out of [0, 1], or
    /// a decision profile that names another decision profile fails the host build with the key to
    /// fix. Whether a named profile exists is checked when the decision profile is first used. The
    /// <see cref="IDecisionModel"/> (<c>AddOrkeonDecisionProvider</c>) is asked only while the decision
    /// circuit is on (<see cref="DecisionCircuit.Of(IServiceProvider)"/>, read at each call); off, the profile is built
    /// without it and takes no decision (#433).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The host configuration.</param>
    /// <returns>The same <paramref name="services"/>.</returns>
    /// <exception cref="InvalidOperationException">The section is invalid.</exception>
    public static IServiceCollection AddOrkeonDecisionProfiles(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Called twice, registers nothing twice: the profiles are LLM profile registrations, and the
        // registry refuses a name it already holds.
        if (services.Any(d => d.ServiceType == typeof(DecisionProfilesMarker)))
            return services;
        services.AddSingleton(new DecisionProfilesMarker());

        services.AddOptions<DecisionProfilesOptions>()
            .Bind(configuration.GetSection(DecisionProfilesOptions.SectionName))
            .DeclareSettings(DecisionProfilesOptions.SectionName);

        var options = new DecisionProfilesOptions();
        configuration.GetSection(DecisionProfilesOptions.SectionName).Bind(options);
        Validate(options);
        foreach (var (name, profile) in options.Profiles)
            services.AddOrkeonLlmProfile(name, sp => Build(sp, name, profile));
        return services;
    }

    /// <summary>Checks the section; throws with the key to fix.</summary>
    /// <param name="options">The bound section.</param>
    /// <exception cref="InvalidOperationException">The section is invalid.</exception>
    internal static void Validate(DecisionProfilesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        foreach (var (name, profile) in options.Profiles)
        {
            var path = $"{DecisionProfilesOptions.SectionName}:Profiles:{name}";
            if (LlmProfiles.IsAuto(name))
                throw new InvalidOperationException(LlmProfiles.AutoReservedMessage(path));
            Named(options, path + ":Target", profile.Target, required: true);
            if (profile.Stages.Count == 0)
                throw new InvalidOperationException($"{path}:Stages: a decision profile needs at least one stage.");
            for (var i = 0; i < profile.Stages.Count; i++)
                ValidateStage(options, $"{path}:Stages:{i}", profile.Stages[i]);
        }
    }

    private static void ValidateStage(DecisionProfilesOptions options, string path, DecisionStageOptions stage)
    {
        switch (KindOf(stage.Kind))
        {
            case RouteKind:
                if (stage.Routes.Count == 0)
                    throw new InvalidOperationException($"{path}:Routes: a route stage needs at least one profile.");
                foreach (var route in stage.Routes.Keys)
                    Named(options, $"{path}:Routes:{route}", route, required: true);
                Named(options, path + ":Fallback", stage.Fallback, required: false);
                Probability(path + ":MinConfidence", stage.MinConfidence);
                break;
            case GuardKind:
                if (stage.Risks.Count == 0)
                    throw new InvalidOperationException($"{path}:Risks: a guard stage needs at least one risk.");
                ParsePhase(path, stage.Phase);
                Probability(path + ":BlockThreshold", stage.BlockThreshold);
                Probability(path + ":WarnThreshold", stage.WarnThreshold);
                break;
            case VerifyKind:
                Named(options, path + ":EscalateTo", stage.EscalateTo, required: true);
                Probability(path + ":MinProbability", stage.MinProbability);
                ParseSteps(path, stage.Steps);
                break;
            default:
                throw new InvalidOperationException(
                    $"{path}:Kind: '{stage.Kind}' is not a stage; expected {RouteKind}, {GuardKind} or {VerifyKind}.");
        }
    }

    /// <summary>
    /// A profile a decision profile names: present when required, and never a decision profile —
    /// one stage of indirection, so no cycle can form.
    /// </summary>
    private static void Named(DecisionProfilesOptions options, string key, string? name, bool required)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            if (required)
                throw new InvalidOperationException($"{key}: a profile name is required.");
            return;
        }

        if (options.Profiles.ContainsKey(name.Trim()))
            throw new InvalidOperationException(
                $"{key}: '{name}' is a decision profile; a decision profile names LLM profiles only.");
    }

    private static readonly string[] s_knownKinds = [RouteKind, GuardKind, VerifyKind];

    /// <summary>The stage kind <paramref name="kind"/> names, whatever its case; itself when it names none.</summary>
    private static string KindOf(string kind)
    {
        var trimmed = kind.Trim();
        return s_knownKinds.FirstOrDefault(known => string.Equals(known, trimmed, StringComparison.OrdinalIgnoreCase))
            ?? trimmed;
    }

    private static void Probability(string key, double value)
    {
        if (value is < 0 or > 1 || double.IsNaN(value))
            throw new InvalidOperationException($"{key}: {value} is not a probability between 0 and 1.");
    }

    private static DecisionStagePhase ParsePhase(string path, string? phase)
    {
        if (string.IsNullOrWhiteSpace(phase))
            return DecisionStagePhase.Both;
        if (Enum.TryParse<DecisionStagePhase>(phase.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
            return parsed;
        throw new InvalidOperationException($"{path}:Phase: '{phase}' is not request, response or both.");
    }

    private static DecisionVerifySteps ParseSteps(string path, string? steps)
    {
        if (string.IsNullOrWhiteSpace(steps))
            return DecisionVerifySteps.Final;
        if (Enum.TryParse<DecisionVerifySteps>(steps.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
            return parsed;
        throw new InvalidOperationException($"{path}:Steps: '{steps}' is not final or all.");
    }

    private static void AddStage(
        DecisionStageOptions stage,
        string stagePath,
        IDecisionModel model,
        ILoggerFactory loggerFactory,
        Func<string, ILlmProvider> resolve,
        List<IDecisionStage> request,
        List<IDecisionStage> response)
    {
        switch (KindOf(stage.Kind))
        {
            case RouteKind:
                foreach (var route in stage.Routes.Keys)
                    resolve(route);
                if (!string.IsNullOrWhiteSpace(stage.Fallback))
                    resolve(stage.Fallback.Trim());
                request.Add(new RouteDecisionStage(model, stage.Routes, stage.MinConfidence, stage.Fallback));
                break;
            case GuardKind:
                var phase = ParsePhase(stagePath, stage.Phase);
                var guard = new GuardDecisionStage(
                    model, stage.Risks, phase, stage.BlockThreshold, stage.WarnThreshold,
                    loggerFactory.CreateLogger<GuardDecisionStage>());
                if (phase != DecisionStagePhase.Response)
                    request.Add(guard);
                if (phase != DecisionStagePhase.Request)
                    response.Add(guard);
                break;
            case VerifyKind:
                resolve(stage.EscalateTo!.Trim());
                response.Add(new VerifyDecisionStage(
                    model, stage.EscalateTo, stage.MinProbability, stage.Criterion, ParseSteps(stagePath, stage.Steps)));
                break;
        }
    }

    private static DecisionLlmProvider Build(IServiceProvider services, string name, DecisionProfileOptions profile)
    {
        var path = $"{DecisionProfilesOptions.SectionName}:Profiles:{name}";
        var registry = services.GetRequiredService<ILlmProfileRegistry>();
        var loggerFactory = services.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;

        ILlmProvider Resolve(string profileName) =>
            registry.IsKnown(profileName)
                ? registry.Resolve(profileName).Provider
                : throw new InvalidOperationException($"{path}: the profile '{profileName}' is not defined.");

        // Built once per host, with the host's circuit; on, the profile reads the circuit again at each call, so a
        // run whose scope is off (DecisionCircuitScope, #431) takes the path of the profile built off.
        var circuit = DecisionCircuit.OfHost(services);
        if (!circuit.Enabled)
            return BuildWithoutDecisions(name, profile, circuit.OffReason!, Resolve, loggerFactory, services.GetService<IAuditLogger>());

        DecisionLlmProvider? off = null;
        DecisionLlmProvider? WhenCircuitOff() =>
            DecisionCircuit.Of(services) is { Enabled: false } now
                ? LazyInitializer.EnsureInitialized(
                    ref off, () => BuildWithoutDecisions(name, profile, now.OffReason!, Resolve, loggerFactory, services.GetService<IAuditLogger>()))
                : null;

        var model = services.GetRequiredService<IDecisionModel>();
        var request = new List<IDecisionStage>();
        var response = new List<IDecisionStage>();
        for (var i = 0; i < profile.Stages.Count; i++)
            AddStage(profile.Stages[i], $"{path}:Stages:{i}", model, loggerFactory, Resolve, request, response);

        return new DecisionLlmProvider(
            name,
            Resolve(profile.Target.Trim()),
            new DecisionStages(request, response),
            Resolve,
            profile.FailClosed,
            loggerFactory.CreateLogger<DecisionLlmProvider>(),
            services.GetService<IAuditLogger>())
        {
            WhenCircuitOff = WhenCircuitOff,
        };
    }

    /// <summary>
    /// The profile with the decision circuit off (#433): no stage asks the model. A route acts as an
    /// answer under its floor - its fallback when it declares one, else the profile so far - a verify
    /// never escalates, and a guard lets the call go on, unless the profile is fail-closed: a guard
    /// that cannot judge then blocks every call, as a failing stage would. Every profile the stages
    /// name is still checked, so a profile that is wrong with the circuit on is wrong with it off.
    /// One warning says it, when the profile is first resolved.
    /// </summary>
    private static DecisionLlmProvider BuildWithoutDecisions(
        string name,
        DecisionProfileOptions profile,
        string reason,
        Func<string, ILlmProvider> resolve,
        ILoggerFactory loggerFactory,
        IAuditLogger? audit)
    {
        var to = profile.Target.Trim();
        var guarded = false;
        foreach (var stage in profile.Stages)
        {
            switch (KindOf(stage.Kind))
            {
                case RouteKind:
                    foreach (var route in stage.Routes.Keys)
                        resolve(route);
                    if (!string.IsNullOrWhiteSpace(stage.Fallback))
                        to = stage.Fallback.Trim();
                    break;
                case GuardKind:
                    guarded = true;
                    break;
                case VerifyKind:
                    resolve(stage.EscalateTo!.Trim());
                    break;
            }
        }

        var target = resolve(profile.Target.Trim());
        var provider = string.Equals(to, profile.Target.Trim(), StringComparison.OrdinalIgnoreCase) ? target : resolve(to);
        var logger = loggerFactory.CreateLogger<DecisionLlmProvider>();
        IDecisionStage[] request = [];
        if (guarded && profile.FailClosed)
        {
            request = [new CircuitOffGuardStage(reason)];
            Orkeon.Infrastructure.Decision.DecisionCircuitLog.ProfileBlocksEveryCall(logger, name, reason);
        }
        else
        {
            Orkeon.Infrastructure.Decision.DecisionCircuitLog.ProfilePassesThrough(logger, name, reason, to);
        }

        return new DecisionLlmProvider(name, provider, new DecisionStages(request, []), resolve, profile.FailClosed, logger, audit);
    }

    /// <summary>Marks a service collection the profiles were added to.</summary>
    private sealed class DecisionProfilesMarker;
}
