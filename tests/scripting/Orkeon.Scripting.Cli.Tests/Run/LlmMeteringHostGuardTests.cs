using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Hosting;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Scripting.Cli.Tests.Doubles;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>
/// Architecture guard, behavioural half (STUDIO-42 D-04): every LLM surface the runner host
/// hands the runtime — <see cref="ILlmProvider"/>, <see cref="IBasicLlmProvider"/>,
/// <see cref="IChatClient"/> — calls through the meter, whether the host runs on the echo
/// provider or on a configured one. The source half lives with the Infrastructure tests; this
/// one proves the composition, and turns red on a host that registers a provider around it.
/// <para>
/// In the CLI collection: a host built without an <c>Llm</c> section warns on the
/// process-global stderr.
/// </para>
/// </summary>
[Collection(CliCollection.Name)]
public sealed class LlmMeteringHostGuardTests : IDisposable
{
    private const string OpenAiAnswer =
        """{"id":"chatcmpl-1","object":"chat.completion","model":"gpt-4o-mini","choices":[{"index":0,"message":{"role":"assistant","content":"pong"},"finish_reason":"stop"}],"usage":{"prompt_tokens":9,"completion_tokens":1,"total_tokens":10}}""";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-metering-guard-" + Guid.NewGuid().ToString("N"));

    public LlmMeteringHostGuardTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    /// <summary>
    /// Calls each surface once and names the ones that reached no meter — the guard's verdict,
    /// empty on a host that keeps every provider on the metered path.
    /// </summary>
    private static async Task<IReadOnlyList<string>> UnmeteredSurfacesAsync(IServiceProvider services, RecordingUsageSink sink)
    {
        var ct = TestContext.Current.CancellationToken;
        var unmetered = new List<string>();

        async Task CheckAsync(string surface, Func<Task> call)
        {
            var before = sink.Count;
            await call();
            if (sink.Count != before + 1)
                unmetered.Add(surface);
        }

        await CheckAsync(nameof(ILlmProvider), () => services.GetRequiredService<ILlmProvider>().ChatAsync([LlmMessage.User("ping")], cancellationToken: ct));
        await CheckAsync(nameof(IBasicLlmProvider), () => services.GetRequiredService<IBasicLlmProvider>().ChatAsync("ping", cancellationToken: ct));
        await CheckAsync(nameof(IChatClient), () => services.GetRequiredService<IChatClient>().GetResponseAsync("ping", cancellationToken: ct));
        return unmetered;
    }

    private static IHost BuildHost(string? settingsPath, Action<IServiceCollection> configure)
    {
        using var console = new TestConsole();
        return RunnerHost.Build(
            settingsPath,
            new RunnerMountPlan(),
            configureLogging: (_, logging) => logging.SetMinimumLevel(LogLevel.None),
            configureServices: (_, services) => configure(services));
    }

    [Fact]
    public async Task Every_surface_of_the_echo_provider_is_metered()
    {
        var sink = new RecordingUsageSink();
        using var host = BuildHost(settingsPath: null, services => services.AddSingleton<ILlmUsageSink>(sink));

        Assert.Empty(await UnmeteredSurfacesAsync(host.Services, sink));

        // The echo calls no model and says so: every reading is a counted zero.
        Assert.All(sink.Events, usage =>
        {
            Assert.Equal("undefined", usage.Provider);
            Assert.False(usage.Estimated);
            Assert.Equal(0, usage.PromptTokens + usage.CompletionTokens);
        });
    }

    [Fact]
    public async Task Every_surface_of_a_configured_provider_is_metered()
    {
        var settings = Path.Combine(_root, "appsettings.json");
        await File.WriteAllTextAsync(
            settings,
            """{ "Llm": { "Model": "gpt-4o-mini", "BaseUrl": "https://api.openai.com/v1", "ApiKey": "sk-test" } }""",
            TestContext.Current.CancellationToken);
        var sink = new RecordingUsageSink();
        using var vendor = new StubHttpMessageHandler(OpenAiAnswer);
        using var host = BuildHost(settings, services =>
        {
            services.AddSingleton<ILlmUsageSink>(sink);
            services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory(vendor));
        });

        Assert.Empty(await UnmeteredSurfacesAsync(host.Services, sink));
        Assert.Equal(vendor.Requests.Count, sink.Count);
        Assert.All(sink.Events, usage => Assert.Equal(10, usage.PromptTokens + usage.CompletionTokens));
        Assert.IsType<OpenAIProvider>(MeteredLlmProvider.Unwrap(host.Services.GetRequiredService<ILlmProvider>()));
    }

    [Fact]
    public async Task A_provider_registered_around_the_path_turns_the_guard_red()
    {
        // The counter-example: a host that hands the runtime a provider of its own, bare.
        var sink = new RecordingUsageSink();
        using var host = BuildHost(settingsPath: null, services =>
        {
            services.AddSingleton<ILlmUsageSink>(sink);
            services.AddSingleton<IBasicLlmProvider>(new LlmProviderAdapter(new FakeBillingLlmProvider()));
        });

        Assert.Equal([nameof(IBasicLlmProvider)], await UnmeteredSurfacesAsync(host.Services, sink));
    }

    /// <summary>Keeps every usage event, thread-safe.</summary>
    private sealed class RecordingUsageSink : ILlmUsageSink
    {
        private readonly Lock _gate = new();
        private readonly List<CostUsageEvent> _events = [];

        public int Count
        {
            get { lock (_gate) return _events.Count; }
        }

        public IReadOnlyList<CostUsageEvent> Events
        {
            get { lock (_gate) return [.. _events]; }
        }

        public void Record(CostUsageEvent usage)
        {
            lock (_gate)
                _events.Add(usage);
        }
    }
}
