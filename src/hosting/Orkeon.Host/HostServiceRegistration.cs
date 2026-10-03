using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Configuration;
using Orkeon.Application.Interfaces.AgentCommunication;
using Orkeon.Infrastructure.AgentCommunication;
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
    /// registry and runner, the per-run progress hook, the chat channel, the A2A server when
    /// <c>Orkeon:Host:A2A</c> turns it on, and the hosted services
    /// (<see cref="AddHostLifetimeServices"/>).
    /// </summary>
    public static IServiceCollection AddHostServices(
        this IServiceCollection services,
        IConfiguration configuration,
        HostCrewMountPlan crewPlan)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(crewPlan);

        // The daemon's sections — Orkeon:Host with its A2A, and Discord below — are read once,
        // here, and their instances are what the services get (GAP-35). Bound lazily, a value the
        // binder could not convert escaped the start as a crash when the host built its services;
        // read now, it refuses the start, naming its key (exit 78).
        var hostSection = HostStartup.ReadSection<OrkeonHostOptions>(configuration, OrkeonHostOptions.SectionName);
        services.AddSingleton(Options.Create(hostSection));

        // Their keys are judged with every section the runner host reads (GAP-40): a key the daemon
        // does not know — Orkeon:Host:RunTimeoutt — refuses the start (exit 78), where it used to be
        // read as absent. orkeon run leaves them to the daemon.
        services.DeclareSettingsShape(OrkeonHostOptions.SectionName, typeof(OrkeonHostOptions));
        services.DeclareSettingsShape(Gateway.DiscordChannelOptions.SectionName, typeof(Gateway.DiscordChannelOptions));

        // The crew→virtual-path map travels with the mounts that made it true: CrewRunner
        // loads a hosted crew by its virtual spelling, never by the operator's disk path.
        services.AddSingleton(crewPlan);

        // The generic host caps the WHOLE stop sequence at HostOptions.ShutdownTimeout
        // (default 30 s). Left alone, an operator raising ShutdownGracePeriod past ~25 s
        // silently truncated their own drain — the docs tell them to scale TimeoutStopSec,
        // and the framework then cut them off underneath it. Budget: the grace, the drain's
        // 5 s teardown wait, and 5 s for the channel to disconnect.
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

        // Other agents run the exposed crews over A2A (GAP-23) — only when the operator turns it
        // on: a daemon must not open a port nobody asked for.
        if (hostSection.A2A.Enabled)
            services.AddHostA2A(configuration, hostSection.A2A);

        // One progress hook per run scope: the strategies dispatch task completions into it,
        // and CrewRunner wires its callback to the conversation watching the run. This is
        // what makes "reports progress as tasks finish" true rather than documented.
        services.AddScoped<RunProgressHook>();
        services.AddScoped<Orkeon.Application.Crew.ICrewExecutionHook>(
            sp => sp.GetRequiredService<RunProgressHook>());

        services.AddSingleton(Options.Create(
            HostStartup.ReadSection<Gateway.DiscordChannelOptions>(configuration, Gateway.DiscordChannelOptions.SectionName)));

        // The MCP connection, the A2A server, the chat channel, the crew host — in that order,
        // which is both start order and stop order reversed.
        services.AddHostLifetimeServices();
        return services;
    }

    /// <summary>
    /// The A2A server of the exposed crews (GAP-23). The host's router first: it is what the
    /// card lists and what a task runs through, and <c>AddOrkeonA2A</c> registers its own agent
    /// router — and the process-wide agent directory that router reads, which would share every
    /// run's agents with the next — only when no router is.
    /// </summary>
    private static void AddHostA2A(this IServiceCollection services, IConfiguration configuration, HostA2AOptions a2a)
    {
        services.AddSingleton<IA2ATaskRouter, HostedCrewA2ARouter>();

        // The A2A section as the daemon means it: the listener from Orkeon:Host:A2A, and no
        // EnableServer — which would register a hosted service starting a second server, at the
        // wrong place in the start order; HostA2AService starts the one registered below. The rest
        // of the section is read as written: the card's identity, A2A:Security and the bearer
        // validators its AzureAD/Oidc subsections declare. An EnableServer, Host or Port the
        // operator wrote under A2A is refused at start (HostA2AService), not overridden in silence.
        var a2aConfiguration = new ConfigurationBuilder()
            .AddConfiguration(configuration)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["A2A:EnableServer"] = "false",
                ["A2A:Host"] = a2a.Host,
                ["A2A:Port"] = a2a.Port.ToString(CultureInfo.InvariantCulture),
            })
            .Build();

        // Read at start, like the daemon's own sections (GAP-35): AddOrkeonA2A binds the A2A section
        // and its bearer validators now, and A2A:Security is bound when the server is built — a
        // value the binder cannot convert crashed the start there. Read here, it refuses it (78).
        _ = HostStartup.ReadSection<A2ASecurityOptions>(a2aConfiguration, "A2A:Security");
        try
        {
            services.AddOrkeonA2A(a2aConfiguration);
        }
        catch (InvalidOperationException ex)
        {
            throw new HostConfigurationException(ex.Message, ex);
        }

        services.TryAddSingleton<IA2AServer, A2AServer>();
    }

    /// <summary>
    /// Registers the hosted services. They start in this order and stop in the reverse one:
    /// <list type="number">
    /// <item><see cref="McpConnectionService"/> first, so the MCP servers' tools are in the
    /// registry before the channel can deliver a message that loads a crew, and so the servers
    /// stay connected until every run has drained (GAP-11);</item>
    /// <item><see cref="HostA2AService"/>, after MCP for the same reason — a task loads a crew —
    /// and before the chat channel and the crew host, so it stops after the drain and a run in
    /// flight still answers the peer that asked for it (GAP-23);</item>
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
        services.AddHostedService<HostA2AService>();
        services.AddHostedService<Gateway.ChatChannelService>();
        services.AddHostedService<CrewHostService>();
        return services;
    }
}
