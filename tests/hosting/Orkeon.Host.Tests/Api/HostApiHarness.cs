using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Host.Tests.Doubles;
using Orkeon.Hosting;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Tests.Shared.Network;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Host.Tests.Api;

/// <summary>
/// The daemon of the host API tests: the host <c>Program</c> builds (the runner host plus <c>AddHostServices</c>, scope
/// validation on), one crew <c>support</c>, the run history on as in the daemon, the API on a free loopback port taken
/// through <see cref="LoopbackPorts"/>. The model is a double. A harness owns one folder: starting it twice makes a
/// restart over the same history.
/// </summary>
internal sealed class HostApiHarness : IDisposable
{
    public HostApiHarness()
    {
        Directory.CreateDirectory(Path.Combine(Root, "crews"));
        File.WriteAllText(CrewPath, """
            name: support
            goal: Answer one question offline
            process: sequential
            agents:
              helper:
                role: Helper
                goal: Help
                backstory: A minimal test agent.
                maxIter: 1
            tasks:
              answer:
                description: Answer the question.
                expected_output: An answer.
                agent: helper
            """);
    }

    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public string Root { get; } = Path.Combine(Path.GetTempPath(), $"orkeon-host-api-{Guid.NewGuid():N}");

    private string CrewPath => Path.Combine(Root, "crews", "support.yaml");

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    /// <summary>Starts the daemon. <paramref name="configure"/> edits the settings root and the <c>Api</c> section before they are written.</summary>
    public async Task<Daemon> StartAsync(
        HeldLlmProvider llm,
        int maxRuns = 4,
        Action<Dictionary<string, object?>, Dictionary<string, object?>>? configure = null,
        Action<IServiceCollection>? services = null,
        ILoggerProvider? logs = null)
    {
        var (host, port) = await LoopbackPorts.StartAsync(
            free => Build(llm, free, maxRuns, configure, services, logs), static (built, ct) => built.StartAsync(ct), Ct);
        return new Daemon(host, port);
    }

    /// <summary>The daemon, built and not started.</summary>
    public IHost Build(
        HeldLlmProvider llm,
        int port,
        int maxRuns = 4,
        Action<Dictionary<string, object?>, Dictionary<string, object?>>? configure = null,
        Action<IServiceCollection>? services = null,
        ILoggerProvider? logs = null)
    {
        var api = new Dictionary<string, object?>
        {
            ["Enabled"] = true,
            ["Prefix"] = $"{LoopbackPorts.Host}:{port}/",
        };
        var root = new Dictionary<string, object?>
        {
            ["RaggableTree"] = new { Enabled = false },
            ["Orkeon"] = new Dictionary<string, object?>
            {
                ["Host"] = new Dictionary<string, object?>
                {
                    ["Crews"] = new object[] { new { Name = "support", Path = CrewPath, Profile = new { MaxConcurrentRuns = maxRuns } } },
                    ["Api"] = api,
                },
            },
        };
        configure?.Invoke(root, api);

        var settings = Path.Combine(Root, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(settings, JsonSerializer.Serialize(root));
        var crewPlan = HostCrewMounts.For([new HostedCrewOptions { Name = "support", Path = CrewPath }]);

        return RunnerHost.Build(
            settings,
            new RunnerMountPlan
            {
                CliMounts = [.. crewPlan.Mounts],
                AllowExternalMounts = true,
                WorkingDirectory = Root,
                RunHistoryByDefault = true,
            },
            configureLogging: (_, logging) =>
            {
                logging.ClearProviders();
                if (logs is not null)
                    logging.SetMinimumLevel(LogLevel.Debug).AddProvider(logs);
            },
            configureServices: (context, collection) =>
            {
                collection.AddHostServices(context.Configuration, crewPlan);
                collection.AddOrkeonLlmProvider(_ => llm);
                services?.Invoke(collection);
            },
            configureBuilder: builder => builder.UseDefaultServiceProvider(options =>
            {
                options.ValidateScopes = true;
                options.ValidateOnBuild = true;
            }));
    }

    /// <summary>A running daemon and its address.</summary>
    internal sealed class Daemon(IHost host, int port) : IAsyncDisposable
    {
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };

        public IHost Host => host;

        public CrewHostRegistry Registry => host.Services.GetRequiredService<CrewHostRegistry>();

        public Uri Url(string path) => new($"{LoopbackPorts.Host}:{port}{path}");

        public Task<HttpResponseMessage> GetAsync(string path, string? authorization = null) => SendAsync(HttpMethod.Get, path, null, authorization);

        public Task<HttpResponseMessage> PostAsync(string path, object? body, string? authorization = null) => SendAsync(HttpMethod.Post, path, body, authorization);

        public Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, string? authorization = null, Action<HttpRequestMessage>? configure = null)
        {
#pragma warning disable CA2000 // The HttpClient disposes the request it sends.
            var request = new HttpRequestMessage(method, Url(path));
#pragma warning restore CA2000
            if (body is not null)
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            else if (method == HttpMethod.Post)
                request.Content = new StringContent(string.Empty, Encoding.UTF8, "application/json");
            if (authorization is not null)
                request.Headers.TryAddWithoutValidation("Authorization", authorization);
            configure?.Invoke(request);
            return _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);
        }

        /// <summary>POSTs a run and returns the response.</summary>
        public Task<HttpResponseMessage> StartRunAsync(string prompt = "What changed?", string? idempotencyKey = null, string crew = "support", string? authorization = null, object? variables = null) =>
            PostAsync("/runs", new { crew, prompt, variables, idempotency_key = idempotencyKey }, authorization);

        public static async Task<JsonObject> JsonAsync(HttpResponseMessage response) =>
            JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!.AsObject();

        /// <summary>Waits until the run reads <paramref name="status"/> over the API.</summary>
        public async Task WaitForStatusAsync(string runId, string status)
        {
            await Polling.WaitUntilAsync(async () =>
            {
                using var response = await GetAsync($"/runs/{runId}");
                return response.IsSuccessStatusCode && (await JsonAsync(response))["status"]?.GetValue<string>() == status;
            }, TimeSpan.FromSeconds(60));
        }

        /// <summary>How many runs the history holds.</summary>
        public async Task<int> RunCountAsync() =>
            (await host.Services.GetRequiredService<Orkeon.Application.Runs.IRunHistoryReader>().ListAsync(new Orkeon.Application.Runs.RunQuery(), Ct)).Count;

        public async ValueTask DisposeAsync()
        {
            if (!host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested)
                await host.StopAsync(CancellationToken.None);
            host.Dispose();
            _http.Dispose();
        }
    }
}
