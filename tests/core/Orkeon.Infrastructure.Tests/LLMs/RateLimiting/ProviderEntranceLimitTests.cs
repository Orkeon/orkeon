using System.Net;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Configuration;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Crew;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Security;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using CrewInput = Orkeon.Application.Interfaces.Services.CrewInput;

namespace Orkeon.Infrastructure.Tests.LLMs.RateLimiting;

/// <summary>
/// GAP-38, decision 5 — the host's <c>RateLimiting</c> caps every model call once, at the points
/// that meter a provider: the factory, <c>AddOrkeonLlmProvider</c> and <c>AddOrkeonLlmProfile</c>,
/// the profile registry's <c>ForProvider</c>, the manager's resolver (twice) and the planner of a C#
/// crew. A call takes one lease whatever its path — an agent's turn, the manager, the plan — and
/// never two: a provider passed through two entrances is limited once, a call made inside another
/// limited call takes none, and a provider that runs its own tools is not limited itself — what it
/// calls of Orkeon's is.
/// </summary>
public sealed class ProviderEntranceLimitTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LlmMessage[] Ping => [LlmMessage.User("ping")];

    private const string OpenAiAnswer =
        """{"id":"chatcmpl-1","object":"chat.completion","model":"gpt-4o-mini","choices":[{"index":0,"message":{"role":"assistant","content":"hello"},"finish_reason":"stop"}],"usage":{"prompt_tokens":12,"completion_tokens":3,"total_tokens":15}}""";

    private static ServiceCollection Services(ILlmRateLimiter limiter, ILlmProvider @default)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create));
        services.AddOrkeonLlmProvider(_ => @default);
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        services.AddSingleton(limiter);
        return services;
    }

    // 13 ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_hierarchical_run_with_planning_takes_one_lease_per_model_call()
    {
        var limiter = new CountingLlmRateLimiter();
        var vendor = new ClockedLlmVendor(() => TimeSpan.Zero);
        await using var container = Services(limiter, vendor).BuildServiceProvider();
        await using var scope = container.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var chef = new AgentBuilder().Role("Chef").Goal("Lead the team").Build();
        var writer = new AgentBuilder().Role("Writer").Goal("Write").Build();
        var tasks = new[]
        {
            new CrewTaskBuilder().Description("Draft the article").ExpectedOutput("A draft").Build(),
            new CrewTaskBuilder().Description("Proofread the article").ExpectedOutput("A clean text").Build(),
        };
        var crew = new CrewBuilder().Goal("Ship the article").Hierarchical(chef).Planning()
            .WithAgents([chef, writer]).WithTasks(tasks).Build();
        await sp.GetRequiredService<IAgentRepository>().AddAsync(chef, Ct);
        await sp.GetRequiredService<IAgentRepository>().AddAsync(writer, Ct);
        foreach (var task in tasks)
            await sp.GetRequiredService<ITaskRepository>().AddAsync(task, Ct);
        await sp.GetRequiredService<ICrewRepository>().AddAsync(crew, Ct);

        var output = await sp.GetRequiredService<ICrewOrchestrationService>()
            .KickoffAsync(crew.Id, new CrewInput("article", new Dictionary<string, object>()), Ct);

        Assert.True(output.Succeeded, output.Error);
        Assert.Equal(["plan", "assign", "task", "review", "assign", "task", "review"], vendor.Calls.Select(c => c.Kind));
        Assert.Equal(vendor.Calls.Count, limiter.Acquired);
    }

    [Fact]
    public async Task The_factory_limits_every_provider_it_builds()
    {
        var limiter = new CountingLlmRateLimiter();
        using var http = new MockHttpClientFactory();
        http.SetupDefaultHandler().SetResponseFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(OpenAiAnswer, Encoding.UTF8, "application/json"),
        });
        var factory = new LlmProviderFactory(http, NullLoggerFactory.Instance, usageSink: null, rateLimiter: limiter);
#pragma warning disable CS0618 // ApiKey is the direct-from-config credential path the providers still read.
        var config = LlmConfig.Create("gpt-4o-mini") with { ApiKey = "sk-test", BaseUrl = new Uri("https://api.openai.com/v1") };
#pragma warning restore CS0618

        var basic = Assert.IsType<LlmProviderAdapter>(factory.Create(config));
        await basic.ChatAsync("hello", cancellationToken: Ct);

        Assert.Equal(1, limiter.Acquired);
        Assert.IsType<OpenAIProvider>(MeteredLlmProvider.Unwrap(basic.UnderlyingProvider));
    }

    [Fact]
    public async Task A_provider_registered_by_hand_or_as_a_profile_is_limited_on_every_surface()
    {
        var limiter = new CountingLlmRateLimiter();
        var services = Services(limiter, new MockLlmProvider { Name = "default-vendor" });
        services.AddOrkeonLlmProfile("b", _ => new MockLlmProvider { Name = "vendor-b" });
        await using var container = services.BuildServiceProvider();

        await container.GetRequiredService<ILlmProvider>().ChatAsync(Ping, cancellationToken: Ct);
        await container.GetRequiredService<IBasicLlmProvider>().ChatAsync("ping", cancellationToken: Ct);
        await container.GetRequiredService<IChatClient>().GetResponseAsync("ping", cancellationToken: Ct);
        await container.GetRequiredService<ILlmProfileRegistry>().Resolve("b").Provider.ChatAsync(Ping, cancellationToken: Ct);

        Assert.Equal(["default-vendor", "default-vendor", "default-vendor", "vendor-b"], limiter.Providers);
    }

    [Fact]
    public async Task An_agents_own_provider_and_the_managers_are_limited_where_the_run_resolves_them()
    {
        var limiter = new CountingLlmRateLimiter();
        await using var container = Services(limiter, new MockLlmProvider { Name = "default-vendor" }).BuildServiceProvider();
        var registry = container.GetRequiredService<ILlmProfileRegistry>();
        var resolver = container.GetRequiredService<ManagerLlmResolver>();
        var own = new MockLlmProvider { Name = "agent-own" };
        var managerLlm = new MockLlmProvider { Name = "crew-manager" };
        var managerOwn = new MockLlmProvider { Name = "manager-own" };
        var chef = new AgentBuilder().Role("Chef").Goal("Lead").WithLlm(managerOwn).Build();
        var withManagerLlm = new CrewBuilder().Goal("Ship").Hierarchical().WithManagerLlm(managerLlm)
            .WithAgent(new AgentBuilder().Role("Writer").Goal("Write").Build()).Build();
        var withManagerAgent = new CrewBuilder().Goal("Ship").Hierarchical(chef).WithAgent(chef).Build();

        await registry.ForProvider(own).Provider.ChatAsync(Ping, cancellationToken: Ct);
        await resolver.Resolve(withManagerLlm, managerAgent: null).ChatClient.GetResponseAsync("ping", cancellationToken: Ct);
        await resolver.Resolve(withManagerAgent, chef).ChatClient.GetResponseAsync("ping", cancellationToken: Ct);

        Assert.Equal(["agent-own", "crew-manager", "manager-own"], limiter.Providers);
    }

    [Fact]
    public async Task The_planner_a_csharp_crew_is_given_is_limited_where_the_run_resolves_it()
    {
        var limiter = new CountingLlmRateLimiter();
        var planner = new ClockedLlmVendor(() => TimeSpan.Zero, name: "planner-vendor");
        await using var container = Services(limiter, new ClockedLlmVendor(() => TimeSpan.Zero)).BuildServiceProvider();
        await using var scope = container.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var writer = new AgentBuilder().Role("Writer").Goal("Write").Build();
        var task = new CrewTaskBuilder().Description("Write").ExpectedOutput("Text").AssignTo(writer).Build();
        var crew = new CrewBuilder().Goal("Write").Planning().WithPlanningLlm(planner)
            .WithAgent(writer).WithTask(task).Build();
        await sp.GetRequiredService<IAgentRepository>().AddAsync(writer, Ct);
        await sp.GetRequiredService<ITaskRepository>().AddAsync(task, Ct);
        await sp.GetRequiredService<ICrewRepository>().AddAsync(crew, Ct);

        var output = await sp.GetRequiredService<ICrewOrchestrationService>()
            .KickoffAsync(crew.Id, new CrewInput("write", new Dictionary<string, object>()), Ct);

        Assert.True(output.Succeeded, output.Error);
        Assert.Equal("plan", Assert.Single(planner.Calls).Kind);
        Assert.Equal(1, limiter.AcquiredFor("planner-vendor"));
    }

    [Fact]
    public async Task A_provider_passed_through_two_entrances_is_limited_once()
    {
        var limiter = new CountingLlmRateLimiter();
        var services = Services(limiter, new MockLlmProvider { Name = "default-vendor" });
        // The default, entered by AddOrkeonLlmProvider, entered again as a profile.
        services.AddOrkeonLlmProfile("again", sp => sp.GetRequiredService<ILlmProvider>());
        await using var container = services.BuildServiceProvider();
        var registry = container.GetRequiredService<ILlmProfileRegistry>();

        await registry.Resolve("again").Provider.ChatAsync(Ping, cancellationToken: Ct);
        await registry.ForProvider(container.GetRequiredService<ILlmProvider>()).Provider.ChatAsync(Ping, cancellationToken: Ct);

        Assert.Equal(2, limiter.Acquired);
    }

    // 14 ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_provider_that_runs_its_own_tools_is_not_limited_what_it_calls_of_orkeon_is()
    {
        var limiter = new CountingLlmRateLimiter();
        await using var container = Services(limiter, new MockLlmProvider { Name = "orkeon-model" }).BuildServiceProvider();
        var bridge = new MockRelayLlmProvider("maf-bridge", container.GetRequiredService<ILlmProvider>())
        {
            Capabilities = new LlmProviderCapabilities { RunsOwnTools = true },
        };

        await container.GetRequiredService<ILlmProfileRegistry>().ForProvider(bridge).Provider.ChatAsync(Ping, cancellationToken: Ct);
        await container.GetRequiredService<ILlmProfileRegistry>().ForProvider(bridge).Provider.ChatAsync(Ping, cancellationToken: Ct);

        Assert.Equal(["orkeon-model", "orkeon-model"], limiter.Providers);
    }

    [Fact]
    public async Task Two_nested_limited_providers_under_one_concurrent_request_do_not_block_each_other()
    {
        using var limiter = new LlmRateLimiter(
            Options.Create(new RateLimitingOptions { MaxConcurrentRequests = 1, QueueLimit = 5 }),
            NullLogger<LlmRateLimiter>.Instance);
        await using var container = Services(limiter, new MockLlmProvider { Name = "orkeon-model" }).BuildServiceProvider();
        // A provider of the host's own code that answers through the host's model: both limited.
        var relay = new MockRelayLlmProvider("relay", container.GetRequiredService<ILlmProvider>());

        var answer = await container.GetRequiredService<ILlmProfileRegistry>().ForProvider(relay).Provider
            .ChatAsync(Ping, cancellationToken: Ct)
            .WaitAsync(TimeSpan.FromSeconds(30), Ct);

        Assert.Equal("mock chat response", answer.Content);
    }
}
