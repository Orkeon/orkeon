using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Tests.Shared.FileSystem;
using Polly;
using CrewInput = Orkeon.Application.Interfaces.Services.CrewInput;
using TestHttpClientFactory = Orkeon.Infrastructure.Tests.TestDoubles.TestHttpClientFactory;
using TestHttpMessageHandler = Orkeon.Infrastructure.Tests.TestDoubles.TestHttpMessageHandler;

namespace Orkeon.Infrastructure.Tests.LLMs.Profiles;

/// <summary>
/// CH-04: a cache a crew declares in YAML reaches the Anthropic request as a cache breakpoint.
/// </summary>
public sealed class TaskCacheReachesTheVendorTests
{
    private static readonly Uri Endpoint = new("https://llm.example.test/v1");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> RunAsync(string agentLlm, string taskExtra)
    {
        var yaml = $"""
            name: cache
            goal: Answer with the declared cache
            process: sequential
            agents:
              writer:
                role: Writer
                goal: Write the answer
            {agentLlm}
            tasks:
              write:
                description: Write the answer
                expected_output: An answer
                agent: writer
            {taskExtra}
            """;

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            id = "msg_1",
            type = "message",
            role = "assistant",
            content = new[] { new { type = "text", text = "Done." } },
            stop_reason = "end_turn",
            usage = new { input_tokens = 1, output_tokens = 1 },
        }));
        using var client = new HttpClient(handler);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient(nameof(AnthropicLlmProvider), client);
        var configured = LlmConfig.Create("claude-test", "sk-test") with { BaseUrl = Endpoint };
        using var provider = new AnthropicLlmProvider(configured, factory, Policy.NoOpAsync<HttpResponseMessage>());

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create));
        services.AddOrkeonLlmProvider(_ => provider, configured);
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();

        await using var container = services.BuildServiceProvider();
        var config = await container.GetRequiredService<ICrewDefinitionLoader>().LoadFromStringAsync(yaml, Ct);
        var crew = await container.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, Ct);
        var output = await container.GetRequiredService<ICrewOrchestrationService>().KickoffAsync(
            crew.Id, new CrewInput("cache", new Dictionary<string, object>()), Ct);

        Assert.Single(output.TaskOutputs);
        var request = Assert.Single(handler.CapturedRequests);
        using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(Ct));
        return document.RootElement.Clone();
    }

    [Fact]
    [Trait("BR", "CH-04")]
    public async Task ShouldSendACacheBreakpoint_WhenTheAgentAsksForASystemCache()
    {
        var sent = await RunAsync("""
                llm:
                  cache:
                    system: true
            """, "");

        var system = sent.GetProperty("system");
        Assert.Equal(JsonValueKind.Array, system.ValueKind);
        var last = system[system.GetArrayLength() - 1];
        Assert.Equal("ephemeral", last.GetProperty("cache_control").GetProperty("type").GetString());
    }

    [Fact]
    [Trait("BR", "CH-04")]
    public async Task ShouldSendACacheBreakpoint_WhenTheTaskAsksForASystemCache()
    {
        var sent = await RunAsync("", """
                llm_override:
                  cache:
                    system: true
            """);

        var system = sent.GetProperty("system");
        Assert.Equal(JsonValueKind.Array, system.ValueKind);
        var last = system[system.GetArrayLength() - 1];
        Assert.Equal("ephemeral", last.GetProperty("cache_control").GetProperty("type").GetString());
    }

    [Fact]
    [Trait("BR", "CH-04")]
    public async Task ShouldSendNoCacheBreakpoint_WhenTheTaskTurnsTheAgentsCacheOff()
    {
        var sent = await RunAsync("""
                llm:
                  cache:
                    system: true
            """, """
                llm_override:
                  cache:
                    system: false
                    tools: false
            """);

        Assert.DoesNotContain("cache_control", sent.GetRawText());
    }
}
