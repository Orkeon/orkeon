using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Rag.Onnx.DependencyInjection;

namespace Orkeon.Host;

/// <summary>
/// What the daemon adds to the runner host, and its hosted services in the one order that is
/// correct — kept out of <c>Program.cs</c> so a test builds the very host the binary runs and
/// holds the order.
/// </summary>
internal static class HostServiceRegistration
{
    /// <summary>
    /// The daemon's own registrations on top of <c>RunnerHost</c>'s: its options, the crew
    /// registry and runner, the per-run progress hook, the chat channel, and the hosted
    /// services (<see cref="AddHostLifetimeServices"/>).
    /// </summary>
    public static IServiceCollection AddHostServices(
        this IServiceCollection services,
        IConfiguration configuration,
        HostCrewMountPlan crewPlan)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(crewPlan);

        services.Configure<OrkeonHostOptions>(configuration.GetSection(OrkeonHostOptions.SectionName));

        // The crew→virtual-path map travels with the mounts that made it true: CrewRunner
        // loads a hosted crew by its virtual spelling, never by the operator's disk path.
        services.AddSingleton(crewPlan);

        // The generic host caps the WHOLE stop sequence at HostOptions.ShutdownTimeout
        // (default 30 s). Left alone, an operator raising ShutdownGracePeriod past ~25 s
        // silently truncated their own drain — the docs tell them to scale TimeoutStopSec,
        // and the framework then cut them off underneath it. Budget: the grace, the drain's
        // 5 s teardown wait, and 5 s for the channel to disconnect.
        var hostSection = configuration.GetSection(OrkeonHostOptions.SectionName).Get<OrkeonHostOptions>() ?? new OrkeonHostOptions();
        services.Configure<Microsoft.Extensions.Hosting.HostOptions>(
            o => o.ShutdownTimeout = hostSection.ShutdownGracePeriod + TimeSpan.FromSeconds(10));

        // The allow-list of LLM profiles hosted crews may name (GAP-17): third-party crews
        // pick a profile by name, and the operator decides which names answer.
        services.Configure<Orkeon.Infrastructure.LLMs.Profiles.LlmProfileAccessOptions>(
            o => o.AllowedProfiles = hostSection.LlmProfiles);

        // The ONNX cross-encoder the balanced and quality RAG profiles rerank with (GAP-25):
        // RunnerHost registers the RAG subsystem, each binary adds the reranker, so a hosted
        // crew's knowledge: offers the same profiles as orkeon run. Weights are embedded and
        // loaded on first use: a host whose crews never rerank pays nothing.
        services.AddOrkeonOnnxReranker();

        services.AddSingleton<CrewHostRegistry>();
        services.AddSingleton<CrewRunner>();
        services.AddSingleton<ICrewRunner>(sp => sp.GetRequiredService<CrewRunner>());

        // One progress hook per run scope: the strategies dispatch task completions into it,
        // and CrewRunner wires its callback to the conversation watching the run. This is
        // what makes "reports progress as tasks finish" true rather than documented.
        services.AddScoped<RunProgressHook>();
        services.AddScoped<Orkeon.Application.Crew.ICrewExecutionHook>(
            sp => sp.GetRequiredService<RunProgressHook>());

        services.Configure<Gateway.DiscordChannelOptions>(
            configuration.GetSection(Gateway.DiscordChannelOptions.SectionName));

        // The MCP connection, the chat channel, the crew host — in that order, which is both
        // start order and stop order reversed.
        services.AddHostLifetimeServices();
        return services;
    }

    /// <summary>
    /// Registers the hosted services. They start in this order and stop in the reverse one:
    /// <list type="number">
    /// <item><see cref="McpConnectionService"/> first, so the MCP servers' tools are in the
    /// registry before the channel can deliver a message that loads a crew, and so the servers
    /// stay connected until every run has drained (GAP-11);</item>
    /// <item>the chat channel before the crew host, so it stops after it — the drain must run
    /// while the channel can still deliver, or the grace period keeps runs alive to produce
    /// answers nobody can receive;</item>
    /// <item><see cref="CrewHostService"/>, whose stop is the drain.</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddHostLifetimeServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHostedService<McpConnectionService>();
        services.AddHostedService<Gateway.ChatChannelService>();
        services.AddHostedService<CrewHostService>();
        return services;
    }
}
