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
/// GAP-36: a temperature or a <c>top_p</c> a crew declares reaches the vendor whatever its value,
/// and one that nothing declares — not the call, not the agent, not the task, not the profile — is
/// not sent at all. The engine took 0.7 and 1.0 for "not set": an agent asking for 0.7 on a profile
/// configured at 0.2 ran at 0.2, and a host that set no temperature sent 0.7, which the default
/// models of OpenAI and Anthropic refuse.
/// </summary>
public sealed class DeclaredSamplingReachesTheVendorTests
{
    private const string Key = "sk-test";
    private static readonly Uri Endpoint = new("https://llm.example.test/v1");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A real provider over a recording handler: what it sends is what a vendor receives.</summary>
    private sealed class Vendor : IDisposable
    {
        private readonly TestHttpMessageHandler _handler;
        private readonly HttpClient _client;

        public Vendor(LlmConfig configured)
        {
            Configured = configured;
            _handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                choices = new[] { new { message = new { role = "assistant", content = "Done." } } },
                usage = new { total_tokens = 2, prompt_tokens = 1, completion_tokens = 1 },
            }));
            _client = new HttpClient(_handler);
            var factory = new TestHttpClientFactory();
            factory.RegisterClient(nameof(DeepSeekLlmProvider), _client);
            Provider = new DeepSeekLlmProvider(configured, factory, Policy.NoOpAsync<HttpResponseMessage>());
        }

        public LlmConfig Configured { get; }

        public DeepSeekLlmProvider Provider { get; }

        public int Requests => _handler.CapturedRequests.Count;

        public async Task<JsonElement> SentPayloadAsync()
        {
            var request = Assert.Single(_handler.CapturedRequests);
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(Ct));
            return document.RootElement.Clone();
        }

        public void Dispose()
        {
            Provider.Dispose();
            _client.Dispose();
            _handler.Dispose();
        }
    }

    private static LlmConfig Configured(string model) =>
        LlmConfig.Create(model, Key) with { BaseUrl = Endpoint };

    private static async Task RunAsync(string agentLlm, Vendor @default, Vendor b)
    {
        var yaml = $"""
            name: sampling
            goal: Answer on the declared sampling
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
            """;

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create));
        services.AddOrkeonLlmProvider(_ => @default.Provider, @default.Configured);
        services.AddOrkeonLlmProfile("b", _ => b.Provider, b.Configured);
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();

        await using var container = services.BuildServiceProvider();
        var config = await container.GetRequiredService<ICrewDefinitionLoader>().LoadFromStringAsync(yaml, Ct);
        var crew = await container.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, Ct);
        var output = await container.GetRequiredService<ICrewOrchestrationService>().KickoffAsync(
            crew.Id, new CrewInput("sampling", new Dictionary<string, object>()), Ct);

        Assert.Single(output.TaskOutputs);
    }

    [Fact]
    public async Task A_temperature_equal_to_the_old_engine_default_reaches_the_vendor()
    {
        using var @default = new Vendor(Configured("host-model"));
        using var b = new Vendor(Configured("model-of-b") with { Temperature = 0.2 });

        await RunAsync("""
                llm:
                  profile: b
                  temperature: 0.7
            """, @default, b);

        Assert.Equal(0, @default.Requests);
        Assert.Equal(0.7, (await b.SentPayloadAsync()).GetProperty("temperature").GetDouble(), precision: 3);
    }

    [Fact]
    public async Task An_agent_that_sets_no_temperature_runs_on_its_profiles()
    {
        using var @default = new Vendor(Configured("host-model"));
        using var b = new Vendor(Configured("model-of-b") with { Temperature = 0.2 });

        await RunAsync("""
                llm:
                  profile: b
            """, @default, b);

        Assert.Equal(0.2, (await b.SentPayloadAsync()).GetProperty("temperature").GetDouble(), precision: 3);
    }

    [Fact]
    public async Task A_top_p_of_one_reaches_the_vendor()
    {
        using var @default = new Vendor(Configured("host-model"));
        using var b = new Vendor(Configured("model-of-b") with { TopP = 0.5 });

        await RunAsync("""
                llm:
                  profile: b
                  top_p: 1.0
            """, @default, b);

        Assert.Equal(1.0, (await b.SentPayloadAsync()).GetProperty("top_p").GetDouble(), precision: 3);
    }

    [Fact]
    public async Task A_temperature_and_a_top_p_that_nothing_sets_are_not_sent()
    {
        using var @default = new Vendor(Configured("host-model"));
        using var b = new Vendor(Configured("model-of-b"));

        await RunAsync(string.Empty, @default, b);

        var payload = await @default.SentPayloadAsync();
        Assert.False(payload.TryGetProperty("temperature", out _), "no temperature may reach the vendor");
        Assert.False(payload.TryGetProperty("top_p", out _), "no top_p may reach the vendor");
    }
}
