using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Infrastructure.Tests.LLMs.Profiles;

/// <summary>
/// GAP-18: a C# agent whose builder sugar names no model — <c>.Thinking()</c>,
/// <c>.MaxOutputTokens(n)</c> — runs on the host's model. The sugar used to seed OpenAI's
/// default model, which the chat client then forwarded to whatever vendor the host runs: a
/// DeepSeek or Anthropic host answered "model not found".
/// </summary>
public sealed class UnsetModelOnTheHostTests
{
    private static async Task<MockLlmProvider> RunAsync(Func<AgentBuilder, AgentBuilder> configure, CancellationToken ct)
    {
        var host = new MockLlmProvider { Name = "vendor-host" };
        host.SetChatResult(new LlmResponse { Content = "thought through", PromptTokens = 5, CompletionTokens = 2, TokensUsed = 7 });

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create));
        services.AddOrkeonLlmProvider(_ => host, LlmConfig.Create("m-host"));
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();

        await using var container = services.BuildServiceProvider();
        await using var scope = container.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var agent = configure(new AgentBuilder().Role("Thinker").Goal("Think it through")).Build();
        var task = new CrewTaskBuilder().Description("Think about it").ExpectedOutput("A thought").AssignTo(agent).Build();
        var crew = DomainCrew.Create(new CrewCreateOptions { Goal = "Think", ProcessType = ProcessType.Sequential });
        crew.AddAgent(agent.Id);
        crew.AddTask(task.Id);
        await sp.GetRequiredService<IAgentRepository>().AddAsync(agent, ct);
        await sp.GetRequiredService<ITaskRepository>().AddAsync(task, ct);
        await sp.GetRequiredService<ICrewRepository>().AddAsync(crew, ct);

        var output = await sp.GetRequiredService<ICrewOrchestrationService>().KickoffAsync(
            crew.Id, new Orkeon.Application.Interfaces.Services.CrewInput("think", new Dictionary<string, object>()), ct);

        Assert.True(output.Succeeded, output.Error);
        return host;
    }

    [Fact]
    public async Task A_thinking_agent_runs_on_the_hosts_model_with_its_thinking_block()
    {
        var host = await RunAsync(a => a.Thinking(effort: "high"), TestContext.Current.CancellationToken);

        var sent = host.LastChatConfig!;
        Assert.Equal("m-host", sent.Model);
        Assert.Equal(true, sent.Thinking?.Enabled);
        Assert.Equal("high", sent.Thinking?.Effort);
    }

    [Fact]
    public async Task An_agent_capping_its_output_runs_on_the_hosts_model_with_its_cap()
    {
        var host = await RunAsync(a => a.MaxOutputTokens(2000), TestContext.Current.CancellationToken);

        var sent = host.LastChatConfig!;
        Assert.Equal("m-host", sent.Model);
        Assert.Equal(2000, sent.MaxTokens);
    }
}
