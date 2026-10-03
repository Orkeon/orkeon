using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Scripting.Adapters;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Runtime;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Scripting.Tests.Adapters;

/// <summary>
/// GAP-19, decision 3 — a <c>.ork.ts</c> task changes profile like a YAML task:
/// <c>taskBuilder().withProfile(name)</c> sets the task's <c>llm_override</c> profile, the task runs on
/// that host profile and the crew's other tasks stay where they were, and a name the host does not
/// offer fails the load with the list of known ones. Before it, the DSL had no way to move a task.
/// </summary>
public sealed class TaskProfileTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsCrew Crew(string special) => (JsCrew)new JsEngineFactory().Create().Evaluate($$"""
        const writer = agentBuilder().name("writer").role("Writer").goal("Write").build();
        const usual = taskBuilder().name("usual").agent(writer)
            .description("Usual task").expectedOutput("Done").build();
        const special = taskBuilder().name("special").agent(writer)
            .description("Special task").expectedOutput("Done"){{special}}.build();
        crewBuilder().name("profiles").goal("Mostly the default, once another")
            .withAgent(writer).withTasks([usual, special]).build();
        """).ToObject()!;

    private static StubLlmProvider Provider(string name) =>
        new StubLlmProvider { Name = name }.RespondToChatWith(new LlmResponse { Content = $"answer from {name}" });

    private static ServiceProvider Host(StubLlmProvider @default, StubLlmProvider a, StubLlmProvider b)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create));
        services.AddOrkeonLlmProvider(_ => @default, LlmConfig.Create("host-model"));
        services.AddOrkeonLlmProfile("a", _ => a, LlmConfig.Create("model-of-a"));
        services.AddOrkeonLlmProfile("b", _ => b, LlmConfig.Create("model-of-b"));
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        return services.BuildServiceProvider();
    }

    private static string Prompts(StubLlmProvider provider) =>
        string.Join("\n", provider.ChatCalls.SelectMany(call => call).Select(message => message.Content));

    [Fact]
    public void withProfile_sets_the_profile_of_that_task_alone()
    {
        var config = JsCrewConfigurationAdapter.ToConfiguration(Crew(""".withProfile("b")"""));

        Assert.Equal("b", Assert.Single(config.Tasks, t => t.Description == "Special task").LlmOverride!.Profile);
        Assert.Null(Assert.Single(config.Tasks, t => t.Description == "Usual task").LlmOverride);
    }

    [Fact]
    public void withProfile_and_a_response_format_travel_together()
    {
        var config = JsCrewConfigurationAdapter.ToConfiguration(
            Crew(""".withResponseFormat("json_object").withProfile("b")"""));

        var special = Assert.Single(config.Tasks, t => t.Description == "Special task").LlmOverride!;
        Assert.Equal("b", special.Profile);
        Assert.Equal("json_object", special.ResponseFormat!.Type);
    }

    [Theory]
    [InlineData("""withProfile("")""")]
    [InlineData("""withProfile("   ")""")]
    [InlineData("""withProfile(42)""")]
    public void withProfile_takes_a_profile_name(string call)
    {
        var error = Assert.Throws<InvalidScriptException>(() => Crew("." + call));

        Assert.Contains("withProfile", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_task_on_a_profile_runs_there_and_the_other_tasks_stay_on_the_default()
    {
        var (@default, a, b) = (Provider("vendor-default"), Provider("vendor-a"), Provider("vendor-b"));
        await using var host = Host(@default, a, b);
        await using var scope = host.CreateAsyncScope();
        var config = JsCrewConfigurationAdapter.ToConfiguration(Crew(""".withProfile("b")"""));
        var crew = await scope.ServiceProvider.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, Ct);

        var output = await scope.ServiceProvider.GetRequiredService<ICrewOrchestrationService>().KickoffAsync(
            crew.Id, new CrewInput("profiles", new Dictionary<string, object>()), Ct);

        Assert.True(output.Succeeded, output.Error);
        Assert.Contains("Special task", Prompts(b), StringComparison.Ordinal);
        Assert.DoesNotContain("Usual task", Prompts(b), StringComparison.Ordinal);
        Assert.Contains("Usual task", Prompts(@default), StringComparison.Ordinal);
        Assert.DoesNotContain("Special task", Prompts(@default), StringComparison.Ordinal);
        Assert.Empty(a.ChatCalls);
        // No model named on the task: the profile's own.
        Assert.Equal("model-of-b", b.LastConfig!.Model);
    }

    [Fact]
    public async Task An_unknown_profile_fails_the_load_listing_the_known_ones()
    {
        await using var host = Host(Provider("d"), Provider("a"), Provider("b"));
        await using var scope = host.CreateAsyncScope();
        var config = JsCrewConfigurationAdapter.ToConfiguration(Crew(""".withProfile("inconnu")"""));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, Ct));

        Assert.Contains("'inconnu'", error.Message, StringComparison.Ordinal);
        Assert.Contains("Known profiles: default, a, b.", error.Message, StringComparison.Ordinal);
    }
}
