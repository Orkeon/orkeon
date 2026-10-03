using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.AgentCommunication;
using Orkeon.Domain.Agent;
using Orkeon.Domain.FileSystem;
using Orkeon.Host.Tests.Doubles;
using Orkeon.Hosting;
using Orkeon.Infrastructure.DependencyInjection;

namespace Orkeon.Host.Tests;

/// <summary>
/// GAP-23 — <c>orkeon-host</c> exposes to A2A peers the crews <c>Orkeon:Host:A2A</c> names: one
/// skill per exposed crew, and a task is a run of that crew through <see cref="CrewRunner"/> —
/// exactly what a chat message is: under the crew's mounts, inside its concurrency bound,
/// attributed in the log, stopped by <c>DELETE</c>. The host is the one <c>Program</c> builds —
/// the runner host plus <see cref="HostServiceRegistration.AddHostServices"/>, scope validation
/// on — reached over HTTP on a free loopback port; the model is a double.
/// </summary>
public sealed class HostA2ATests : IDisposable
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan s_wait = TimeSpan.FromSeconds(30);
    private static readonly string[] s_apiKeyScheme = ["ApiKey"];
    private static readonly string[] s_peerKeyName = ["A2A_PEER_KEY"];
    private static readonly string[] s_veilleAndAnUndeclaredCrew = ["veille", "billing"];

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"orkeon-host-a2a-{Guid.NewGuid():N}");
    private readonly List<string> _log = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public HostA2ATests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "crews"));
        Directory.CreateDirectory(Path.Combine(_root, "out", "veille"));
        WriteCrew("support");
        WriteCrew("veille");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    // -------------------------------------------------------------------------
    // What a peer sees
    // -------------------------------------------------------------------------

    [Fact]
    public async Task The_card_lists_one_skill_per_exposed_crew_and_none_for_the_others()
    {
        await using var daemon = await StartAsync(new HeldLlmProvider("unused"));
        using var http = NewHttp();

        using var card = JsonDocument.Parse(await http.GetStringAsync(daemon.Url("/.well-known/agent.json"), Ct));

        // veille is exposed, support is hosted but not: a peer cannot tell it exists.
        var skill = Assert.Single(card.RootElement.GetProperty("skills").EnumerateArray());
        Assert.Equal("veille", skill.GetProperty("id").GetString());
        Assert.Equal("veille", skill.GetProperty("name").GetString());
        Assert.Equal("Weekly technology watch.", skill.GetProperty("description").GetString());
    }

    [Fact]
    public async Task A_task_sent_to_an_exposed_crew_runs_it_under_its_mounts_and_completes_with_its_output()
    {
        var llm = new HeldLlmProvider("the watch report");
        var mounts = new SpyFileSystemScope();
        await using var daemon = await StartAsync(llm, mounts: mounts);
        using var http = NewHttp();

        var result = await SendAsync(http, daemon, "watch-1", "veille", "What changed this week?");

        Assert.True(A2ATaskStatus.Completed == result.Status, $"{result.Status}: {result.Error} :: {DebugLog}");
        Assert.Contains("the watch report", result.Output, StringComparison.Ordinal);

        // The input is the run's need: it reached the crew's model.
        Assert.Contains(llm.Prompts, prompt => prompt.Contains("What changed this week?", StringComparison.Ordinal));

        // The run happened in the crew's own mount namespace, as a chat run does.
        var entered = Assert.Single(mounts.Entered);
        Assert.Contains("/output", entered, StringComparer.Ordinal);

        // Attributed like a chat run: logged with its origin, and its seat released.
        Assert.Contains(Log, line => line.Contains("crew 'veille' from a2a:watch-1", StringComparison.Ordinal));
        Assert.Empty(daemon.Host.Services.GetRequiredService<CrewHostRegistry>().Running);
    }

    [Fact]
    public async Task A_crew_that_fails_answers_Failed_with_the_reason()
    {
        await using var daemon = await StartAsync(new HeldLlmProvider("unused", failure: "the model is down"));
        using var http = NewHttp();

        var result = await SendAsync(http, daemon, "watch-2", "veille", "What changed this week?");

        Assert.Equal(A2ATaskStatus.Failed, result.Status);
        Assert.Null(result.Output);
        // The runner's own sentence, as a chat thread gets it: the run id the operator greps the
        // host log for, never the provider's detail (endpoints, paths), which stays in the log.
        Assert.Contains("The run failed", result.Error, StringComparison.Ordinal);
        Assert.Contains("host log", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_skill_id_the_card_does_not_publish_fails_naming_the_published_skills()
    {
        var llm = new HeldLlmProvider("unused");
        await using var daemon = await StartAsync(llm);
        using var http = NewHttp();

        // support is hosted but not exposed; VEILLE is not the published id (exact match).
        foreach (var skillId in new[] { "support", "VEILLE", "nothing" })
        {
            var result = await SendAsync(http, daemon, $"unknown-{skillId}", skillId, "What changed this week?");

            Assert.Equal(A2ATaskStatus.Failed, result.Status);
            Assert.Contains($"'{skillId}'", result.Error, StringComparison.Ordinal);
            Assert.Contains("'veille'", result.Error, StringComparison.Ordinal);
        }

        Assert.Empty(llm.Prompts);
    }

    // -------------------------------------------------------------------------
    // A run like a chat run
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_chat_conversation_holding_the_crews_last_seat_turns_a_peer_away()
    {
        var llm = new HeldLlmProvider("the watch report", held: true);
        await using var daemon = await StartAsync(llm, veilleMaxRuns: 1);
        using var http = NewHttp();
        var runner = daemon.Host.Services.GetRequiredService<ICrewRunner>();

        var chat = runner.RunAsync("veille", "What changed?", "discord:thread-1");
        await llm.Started.Task.WaitAsync(s_wait, Ct);

        // One bound for both channels: refused with the reason, never queued.
        var refused = await SendAsync(http, daemon, "watch-busy", "veille", "And now?");
        Assert.Equal(A2ATaskStatus.Failed, refused.Status);
        Assert.Contains("already running", refused.Error, StringComparison.Ordinal);

        llm.Release();
        Assert.Equal(HostedRunOutcome.Completed, (await chat.WaitAsync(s_wait, Ct)).Outcome);

        // The seat is free again: the same task now runs.
        var accepted = await SendAsync(http, daemon, "watch-free", "veille", "And now?");
        Assert.Equal(A2ATaskStatus.Completed, accepted.Status);
    }

    [Fact]
    public async Task A_peer_holding_the_crews_last_seat_turns_a_chat_conversation_away()
    {
        var llm = new HeldLlmProvider("the watch report", held: true);
        await using var daemon = await StartAsync(llm, veilleMaxRuns: 1);
        using var http = NewHttp();
        var runner = daemon.Host.Services.GetRequiredService<ICrewRunner>();

#pragma warning disable CA2025 // `sending` is awaited below, inside the scope of `http`.
        var sending = SendAsync(http, daemon, "watch-3", "veille", "What changed?");
#pragma warning restore CA2025
        await llm.Started.Task.WaitAsync(s_wait, Ct);

        var chat = await runner.RunAsync("veille", "Me too", "discord:thread-2");
        Assert.Equal(HostedRunOutcome.Busy, chat.Outcome);

        llm.Release();
        Assert.Equal(A2ATaskStatus.Completed, (await sending.WaitAsync(s_wait, Ct)).Status);
    }

    [Fact]
    public async Task Delete_stops_the_run_the_task_started()
    {
        var llm = new HeldLlmProvider("never delivered", held: true);
        await using var daemon = await StartAsync(llm);
        using var http = NewHttp();
        var registry = daemon.Host.Services.GetRequiredService<CrewHostRegistry>();

#pragma warning disable CA2025 // `sending` is awaited below, inside the scope of `http`.
        var sending = SendAsync(http, daemon, "long-1", "veille", "A long watch");
#pragma warning restore CA2025
        await llm.Started.Task.WaitAsync(s_wait, Ct);
        Assert.Single(registry.Running);

        using var cancel = await http.DeleteAsync(daemon.Url("/a2a/tasks/long-1"), Ct);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        var result = await sending.WaitAsync(s_wait, Ct);
        Assert.Equal(A2ATaskStatus.Cancelled, result.Status);
        Assert.Contains("stopped", result.Error, StringComparison.Ordinal);

        // The run itself stopped — its model call was cancelled — and gave its seat back.
        Assert.Equal(1, llm.CancelledCalls);
        Assert.Empty(registry.Running);
    }

    [Fact]
    public async Task An_authenticated_peer_runs_an_exposed_crew_and_an_anonymous_one_is_refused()
    {
        var llm = new HeldLlmProvider("the watch report");
        await using var daemon = await StartAsync(llm, configure: (root, _) =>
        {
            root["A2A"] = new
            {
                Security = new { AllowedAuthSchemes = s_apiKeyScheme, ApiKeySecretNames = s_peerKeyName },
            };
            root["Secrets"] = new { A2A_PEER_KEY = "peer-secret" };
        });
        using var http = NewHttp();

        using (var anonymous = await PostAsync(http, daemon, "anon-1", "veille", "What changed?", authorization: null))
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Empty(llm.Prompts);

        var result = await SendAsync(http, daemon, "peer-1", "veille", "What changed?", authorization: "ApiKey peer-secret");

        Assert.Equal(A2ATaskStatus.Completed, result.Status);
        Assert.Contains("the watch report", result.Output, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------
    // Refused at start (exit 78), before anything listens
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Exposing_a_crew_the_host_does_not_declare_refuses_the_start()
    {
        using var host = Build(new HeldLlmProvider("unused"), (_, a2a) => a2a["Crews"] = s_veilleAndAnUndeclaredCrew).Host;

        var error = await Assert.ThrowsAsync<HostConfigurationException>(() => host.StartAsync(Ct));

        Assert.Contains("'billing'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'support', 'veille'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Enabling_A2A_without_exposing_a_crew_refuses_the_start()
    {
        using var host = Build(new HeldLlmProvider("unused"), (_, a2a) => a2a.Remove("Crews")).Host;

        var error = await Assert.ThrowsAsync<HostConfigurationException>(() => host.StartAsync(Ct));

        Assert.Contains("Orkeon:Host:A2A:Crews", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Listening_beyond_the_loopback_without_authentication_refuses_the_start()
    {
        using var host = Build(new HeldLlmProvider("unused"), (_, a2a) => a2a["Host"] = "http://+").Host;

        var error = await Assert.ThrowsAsync<HostConfigurationException>(() => host.StartAsync(Ct));

        Assert.Contains("A2A:Security:AllowedAuthSchemes", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_A2A_sections_own_server_keys_are_refused_with_the_hosts_replacements()
    {
        // orkeon-host serves A2A from Orkeon:Host:A2A: an A2A:EnableServer or A2A:Port written
        // after the C# hosts' documentation would otherwise be silently ignored.
        using var host = Build(new HeldLlmProvider("unused"), (root, _) => root["A2A"] = new { EnableServer = true, Port = 9 }).Host;

        var error = await Assert.ThrowsAsync<HostConfigurationException>(() => host.StartAsync(Ct));

        Assert.Contains("A2A:EnableServer", error.Message, StringComparison.Ordinal);
        Assert.Contains("A2A:Port", error.Message, StringComparison.Ordinal);
        Assert.Contains("Orkeon:Host:A2A", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_an_A2A_section_the_host_serves_no_A2A_at_all()
    {
        var (host, port) = Build(new HeldLlmProvider("unused"), (root, _) => HostSection(root).Remove("A2A"));
        using var _ = host;

        Assert.Null(host.Services.GetService<IA2AServer>());

        await host.StartAsync(Ct);
        try
        {
            using var http = NewHttp();
            await Assert.ThrowsAsync<HttpRequestException>(
                () => http.GetAsync(new Uri($"http://localhost:{port}/.well-known/agent.json"), Ct));
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task With_A2A_on_each_run_scope_keeps_its_own_agent_repository()
    {
        // The host routes crews, not agents: the A2A agent directory (a process-wide store every
        // scope's repository shares) would make each run's agents visible to the next, and keep
        // one entry per conversation, forever. Each run's scope keeps its own repository.
        using var host = Build(new HeldLlmProvider("unused")).Host;
        var scopes = host.Services.GetRequiredService<IServiceScopeFactory>();
        var agent = new AgentBuilder().Role("Helper").Goal("Help").Build();

        using (var first = scopes.CreateScope())
            await first.ServiceProvider.GetRequiredService<IAgentRepository>().AddAsync(agent, Ct);

        using var second = scopes.CreateScope();
        Assert.Null(await second.ServiceProvider.GetRequiredService<IAgentRepository>().GetByIdAsync(agent.Id, Ct));
    }

    // -------------------------------------------------------------------------
    // The daemon
    // -------------------------------------------------------------------------

    private string DebugLog
    {
        get
        {
            lock (_log)
                return string.Join(" | ", _log.TakeLast(8));
        }
    }

    private IReadOnlyList<string> Log
    {
        get
        {
            lock (_log)
                return [.. _log];
        }
    }

    private string CrewPath(string name) => Path.Combine(_root, "crews", $"{name}.yaml");

    private void WriteCrew(string name) => File.WriteAllText(CrewPath(name), $"""
        name: {name}
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

    private static Dictionary<string, object?> HostSection(Dictionary<string, object?> root) =>
        (Dictionary<string, object?>)((Dictionary<string, object?>)root["Orkeon"]!)["Host"]!;

    /// <summary>
    /// The daemon over two crews — <c>support</c>, and <c>veille</c> with its own <c>/output</c> —
    /// and an <c>Orkeon:Host:A2A</c> section exposing <c>veille</c> on a free port.
    /// <paramref name="configure"/> edits the settings root and the A2A section before they are
    /// written.
    /// </summary>
    private (IHost Host, int Port) Build(
        HeldLlmProvider llm,
        Action<Dictionary<string, object?>, Dictionary<string, object?>>? configure = null,
        int veilleMaxRuns = 4,
        SpyFileSystemScope? mounts = null)
    {
        var port = FreePort();
        var crews = new[]
        {
            new HostedCrewOptions { Name = "support", Path = CrewPath("support") },
            new HostedCrewOptions { Name = "veille", Path = CrewPath("veille") },
        };

        var a2a = new Dictionary<string, object?>
        {
            ["Enabled"] = true,
            ["Port"] = port,
            ["Crews"] = new[] { "veille" },
        };
        var root = new Dictionary<string, object?>
        {
            // Keeps the on-device embedding model (ONNX) out of the host.
            ["RaggableTree"] = new { Enabled = false },
            ["Orkeon"] = new Dictionary<string, object?>
            {
                ["Host"] = new Dictionary<string, object?>
                {
                    ["Crews"] = new object[]
                    {
                        new { Name = "support", Path = CrewPath("support") },
                        new
                        {
                            Name = "veille",
                            Path = CrewPath("veille"),
                            Description = "Weekly technology watch.",
                            Mounts = new[] { $"{FileSystemMount.Quote(Path.Combine(_root, "out", "veille"))}:/output:rw" },
                            Profile = new { MaxConcurrentRuns = veilleMaxRuns },
                        },
                    },
                    ["A2A"] = a2a,
                },
            },
        };
        configure?.Invoke(root, a2a);

        var settings = Path.Combine(_root, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(settings, JsonSerializer.Serialize(root));

        var crewPlan = HostCrewMounts.For(crews);
        var host = RunnerHost.Build(
            settings,
            new RunnerMountPlan { CliMounts = [.. crewPlan.Mounts], AllowExternalMounts = true },
            configureLogging: (_, logging) => logging.AddProvider(new LogSink(_log)),
            configureServices: (context, services) =>
            {
                services.AddHostServices(context.Configuration, crewPlan);
                services.AddOrkeonLlmProvider(_ => llm);
                if (mounts is not null)
                    services.AddSingleton<IFileSystemScope>(mounts);
            },
            configureBuilder: builder => builder.UseDefaultServiceProvider(options =>
            {
                options.ValidateScopes = true;
                options.ValidateOnBuild = true;
            }));

        return (host, port);
    }

    private async Task<RunningHost> StartAsync(
        HeldLlmProvider llm,
        Action<Dictionary<string, object?>, Dictionary<string, object?>>? configure = null,
        int veilleMaxRuns = 4,
        SpyFileSystemScope? mounts = null)
    {
        var (host, port) = Build(llm, configure, veilleMaxRuns, mounts);
        try
        {
            await host.StartAsync(Ct);
        }
        catch
        {
            host.Dispose();
            throw;
        }

        return new RunningHost(host, port);
    }

    private sealed class RunningHost(IHost host, int port) : IAsyncDisposable
    {
        public IHost Host => host;

        public Uri Url(string path) => new($"http://localhost:{port}{path}");

        public async ValueTask DisposeAsync()
        {
            await host.StopAsync(CancellationToken.None);
            host.Dispose();
        }
    }

    private static HttpClient NewHttp() => new() { Timeout = TimeSpan.FromSeconds(60) };

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient http, RunningHost daemon, string id, string skillId, string input, string? authorization)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, daemon.Url("/a2a/tasks/send"))
        {
            Content = new StringContent(JsonSerializer.Serialize(new { id, skillId, input }), Encoding.UTF8, "application/json"),
        };
        if (authorization is not null)
            request.Headers.TryAddWithoutValidation("Authorization", authorization);

        return await http.SendAsync(request, Ct);
    }

    private static async Task<A2ATaskResponse> SendAsync(
        HttpClient http, RunningHost daemon, string id, string skillId, string input, string? authorization = null)
    {
        using var response = await PostAsync(http, daemon, id, skillId, input, authorization);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonSerializer.Deserialize<A2ATaskResponse>(await response.Content.ReadAsStringAsync(Ct), s_json)!;
    }

    /// <summary>Every log line at Information or above, with its category.</summary>
    private sealed class LogSink(List<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Logger(sink, categoryName);

        public void Dispose()
        {
            // Nothing to release: the lines live in the test's list.
        }

        private sealed class Logger(List<string> sink, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                    return;
                lock (sink)
                    sink.Add($"{category}: {formatter(state, exception)} {exception?.Message}");
            }
        }
    }
}
