using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Configuration;
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
    /// fix. Whether a named profile exists is checked when the decision profile is first used, and
    /// so is the <see cref="IDecisionModel"/> (<c>AddOrkeonTypeSafe</c>).
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

    /// <summary>The stage kind <paramref name="kind"/> names, whatever its case; itself when it names none.</summary>
    private static string KindOf(string kind)
    {
        var trimmed = kind.Trim();
        foreach (var known in (string[])[RouteKind, GuardKind, VerifyKind])
        {
            if (string.Equals(known, trimmed, StringComparison.OrdinalIgnoreCase))
                return known;
        }

        return trimmed;
    }

    private static void Probability(string key, double value)
    {
        if (value is < 0 or > 1 || double.IsNaN(value))
            throw new InvalidOperationException($"{key}: {value} is not a probability between 0 and 1.");
    }

    private static DecisionStagePhase ParsePhase(string path, string? phase) =>
        string.IsNullOrWhiteSpace(phase)
            ? DecisionStagePhase.Both
            : Enum.TryParse<DecisionStagePhase>(phase.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
                ? parsed
                : throw new InvalidOperationException($"{path}:Phase: '{phase}' is not request, response or both.");

    private static DecisionLlmProvider Build(IServiceProvider services, string name, DecisionProfileOptions profile)
    {
        var path = $"{DecisionProfilesOptions.SectionName}:Profiles:{name}";
        var registry = services.GetRequiredService<ILlmProfileRegistry>();
        var model = services.GetService<IDecisionModel>()
            ?? throw new InvalidOperationException(
                $"{path}: no decision model is registered; call AddOrkeonTypeSafe (section Orkeon:TypeSafe).");
        var loggerFactory = services.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;

        ILlmProvider Resolve(string profileName) =>
            registry.IsKnown(profileName)
                ? registry.Resolve(profileName).Provider
                : throw new InvalidOperationException($"{path}: the profile '{profileName}' is not defined.");

        var request = new List<IDecisionStage>();
        var response = new List<IDecisionStage>();
        for (var i = 0; i < profile.Stages.Count; i++)
        {
            var stage = profile.Stages[i];
            var stagePath = $"{path}:Stages:{i}";
            switch (KindOf(stage.Kind))
            {
                case RouteKind:
                    foreach (var route in stage.Routes.Keys)
                        Resolve(route);
                    if (!string.IsNullOrWhiteSpace(stage.Fallback))
                        Resolve(stage.Fallback.Trim());
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
                    Resolve(stage.EscalateTo!.Trim());
                    response.Add(new VerifyDecisionStage(model, stage.EscalateTo, stage.MinProbability, stage.Criterion));
                    break;
            }
        }

        return new DecisionLlmProvider(
            name,
            Resolve(profile.Target.Trim()),
            request,
            response,
            Resolve,
            profile.FailClosed,
            loggerFactory.CreateLogger<DecisionLlmProvider>(),
            services.GetService<IAuditLogger>());
    }

    /// <summary>Marks a service collection the profiles were added to.</summary>
    private sealed class DecisionProfilesMarker;
}
