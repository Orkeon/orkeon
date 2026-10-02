using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Crew;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using CrewOutput = Orkeon.Application.Interfaces.Services.CrewOutput;

namespace Orkeon.Infrastructure.Tests.Orchestration;

/// <summary>
/// GAP-29: <c>planning: true</c> plans. The orchestrator ran the planner only when the crew
/// carried a planning provider, which only the C# <c>WithPlanningLlm</c> sets: a YAML crew, or a C#
/// crew calling <c>.Planning(true)</c> alone, planned nothing while the documentation said it
/// did. It plans now on the host's default profile — the planner stays
/// there (GAP-19) — and a plan that fails says why.
/// </summary>
public sealed partial class PlanningOnTheDefaultProfileTests
{
    private const string PlannedYaml = """
        name: planned-report
        goal: Write a short report
        process: sequential
        planning: true
        agents:
          writer:
            role: Writer
            goal: Write the report
        tasks:
          outline:
            description: Outline the report
            expected_output: An outline
            agent: writer
          draft:
            description: Draft the report
            expected_output: A draft
            agent: writer
            dependencies: [outline]
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"^- Task (\S+)\s*$", RegexOptions.Multiline)]
    private static partial Regex PlannedTaskLine();

    /// <summary>
    /// A vendor that plans the tasks its planning prompt lists, in the order it lists them, and
    /// records every call: <c>plan</c>, then <c>task:&lt;name&gt;</c> for each task it runs.
    /// </summary>
    private static MockLlmProvider Vendor(string name, List<string> calls)
    {
        var vendor = new MockLlmProvider { Name = name };
        vendor.SetGenerateFunc((prompt, _) =>
        {
            calls.Add($"plan@{name}");
            var tasks = PlannedTaskLine().Matches(prompt)
                .Select((match, index) => new { task = match.Groups[1].Value, order = index + 1 })
                .ToArray();
            return new LlmResponse
            {
                Content = JsonSerializer.Serialize(new { tasks }),
                PromptTokens = 10,
                CompletionTokens = 5,
                TokensUsed = 15,
            };
        });
        vendor.SetChatFunc((messages, _) =>
        {
            var text = string.Join("\n", messages.Select(m => m.Content));
            calls.Add(text.Contains("Draft the report", StringComparison.Ordinal) ? "task:draft"
                : text.Contains("Outline the report", StringComparison.Ordinal) ? "task:outline"
                : "task:?");
            return new LlmResponse { Content = "done", PromptTokens = 3, CompletionTokens = 1, TokensUsed = 4 };
        });
        return vendor;
    }

    private static ServiceProvider Host(MockLlmProvider @default)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create));
        services.AddOrkeonLlmProvider(_ => @default, LlmConfig.Create("host-model"));
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        return services.BuildServiceProvider();
    }

    private static async Task<CrewOutput> RunYamlAsync(MockLlmProvider @default, string yaml)
    {
        await using var container = Host(@default);
        await using var scope = container.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var config = await sp.GetRequiredService<ICrewDefinitionLoader>().LoadFromStringAsync(yaml, Ct);
        var crew = await sp.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, Ct);
        return await sp.GetRequiredService<ICrewOrchestrationService>().KickoffAsync(
            crew.Id, new Orkeon.Application.Interfaces.Services.CrewInput("report", new Dictionary<string, object>()), Ct);
    }

    private static async Task<CrewOutput> RunBuiltAsync(MockLlmProvider @default, Func<CrewBuilder, CrewBuilder> planning)
    {
        await using var container = Host(@default);
        await using var scope = container.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var writer = new AgentBuilder().Role("Writer").Goal("Write the report").Build();
        var outline = new CrewTaskBuilder().Description("Outline the report").ExpectedOutput("An outline").AssignTo(writer).Build();
        var draft = new CrewTaskBuilder().Description("Draft the report").ExpectedOutput("A draft").AssignTo(writer).Build();
        var crew = planning(new CrewBuilder().Goal("Write a short report").Sequential().WithAgent(writer)).Build();
        crew.AddTask(outline.Id);
        crew.AddTask(draft.Id);

        await sp.GetRequiredService<IAgentRepository>().AddAsync(writer, Ct);
        await sp.GetRequiredService<ITaskRepository>().AddAsync(outline, Ct);
        await sp.GetRequiredService<ITaskRepository>().AddAsync(draft, Ct);
        await sp.GetRequiredService<ICrewRepository>().AddAsync(crew, Ct);

        return await sp.GetRequiredService<ICrewOrchestrationService>().KickoffAsync(
            crew.Id, new Orkeon.Application.Interfaces.Services.CrewInput("report", new Dictionary<string, object>()), Ct);
    }

    [Fact]
    public async Task A_yaml_crew_with_planning_true_plans_on_the_default_profile_then_runs_its_tasks()
    {
        var calls = new List<string>();
        var @default = Vendor("default", calls);

        var output = await RunYamlAsync(@default, PlannedYaml);

        Assert.True(output.Succeeded, output.Error);
        Assert.Equal(["plan@default", "task:outline", "task:draft"], calls);
        // The planner's own sampling, on the profile's model.
        Assert.Equal(0.3, @default.LastGenerateConfig!.Temperature, precision: 3);
        Assert.Equal(string.Empty, @default.LastGenerateConfig.Model);
    }

    [Fact]
    public async Task A_csharp_crew_calling_Planning_alone_plans_on_the_default_profile()
    {
        var calls = new List<string>();

        var output = await RunBuiltAsync(Vendor("default", calls), crew => crew.Planning());

        Assert.True(output.Succeeded, output.Error);
        Assert.Equal(["plan@default", "task:outline", "task:draft"], calls);
    }

    [Fact]
    public async Task A_planning_provider_set_in_csharp_keeps_the_hand()
    {
        var calls = new List<string>();
        var planner = Vendor("planner", calls);

        var output = await RunBuiltAsync(Vendor("default", calls), crew => crew.Planning().WithPlanningLlm(planner));

        Assert.True(output.Succeeded, output.Error);
        Assert.Equal(["plan@planner", "task:outline", "task:draft"], calls);
    }

    [Fact]
    public async Task A_crew_without_planning_makes_no_planning_call()
    {
        var calls = new List<string>();

        var output = await RunYamlAsync(Vendor("default", calls), PlannedYaml.Replace("planning: true", "planning: false", StringComparison.Ordinal));

        Assert.True(output.Succeeded, output.Error);
        Assert.Equal(["task:outline", "task:draft"], calls);
    }

    [Fact]
    public async Task A_plan_that_fails_fails_the_crew_with_the_providers_reason()
    {
        // The planner ignored the provider's error and reported "Invalid plan: Task … is missing
        // from the plan" — the real cause (here a refused key) never appeared anywhere.
        var calls = new List<string>();
        var @default = Vendor("default", calls);
        @default.SetGenerateFunc((_, _) => new LlmResponse
        {
            Content = string.Empty,
            Metadata = new Dictionary<string, object>
            {
                [LlmResponseMetadataKeys.Error] = "DeepSeek API error: Unauthorized - Authentication Fails, Your api key is invalid",
            },
        });

        var output = await RunYamlAsync(@default, PlannedYaml);

        Assert.False(output.Succeeded);
        Assert.Contains("Your api key is invalid", output.Error, StringComparison.Ordinal);
        Assert.Empty(calls);
    }
}
