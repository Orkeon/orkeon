using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orkeon.Cli.Commands.Scripting.Configuration;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.ConsoleApp.Tests.Fakes;
using Orkeon.Domain.SharedKernel;

namespace Orkeon.ConsoleApp.Tests.DependencyInjection;

/// <summary>
/// GAP-29 — <c>orkeon-repl</c> registered its configured provider as <see cref="ILlmProvider"/>
/// and never as a chat client: the <see cref="IChatClient"/> it served was the infrastructure's
/// fallback, a second provider built from an empty configuration — no key, OpenAI's endpoint —
/// and every call through it answered "API key is required". The REPL registers its provider the
/// way <c>orkeon run</c> does now: one instance on the three surfaces, the chat client on the
/// configuration of the <c>Llm</c> section, and the echo provider when there is none. The
/// container is the one <c>Program</c> composes; it is built, never run.
/// </summary>
public sealed class ReplChatClientTests : IDisposable
{
    private const string Answer =
        """{"choices":[{"message":{"role":"assistant","content":"hello back"}}],"usage":{"total_tokens":2,"prompt_tokens":1,"completion_tokens":1}}""";

    private readonly string _workspace = Directory.CreateTempSubdirectory("orkeon-repl-llm-").FullName;
    private readonly CapturingHttpClientFactory _http = new(Answer);

    public void Dispose()
    {
        _http.Dispose();
        Directory.Delete(_workspace, recursive: true);
    }

    private IHost Repl(params (string Key, string Value)[] settings) =>
        new HostBuilder()
            .ConfigureAppConfiguration(builder => builder.AddInMemoryCollection(
                settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value))))
            .ConfigureServices((context, services) =>
            {
                Program.ConfigureServices(
                    context, services, UiMode.Plain,
                    ScriptedCommandsCliOptions.Parse(["--mount", $"{_workspace}:/workspace:ro"]),
                    replWordWrap: false);
                // The wire, captured: every provider the container builds sends through it.
                services.AddSingleton<IHttpClientFactory>(_http);
            })
            .Build();

    [Fact]
    public async Task The_chat_client_carries_the_key_url_and_timeout_of_the_Llm_section()
    {
        using var host = Repl(
            ("Llm:BaseUrl", "https://api.deepseek.com/v1"),
            ("Llm:ApiKey", "sk-repl-key"),
            ("Llm:Model", "deepseek-chat"),
            ("Llm:TimeoutSeconds", "600"));

        var response = await host.Services.GetRequiredService<IChatClient>()
            .GetResponseAsync("hello", new ChatOptions { Temperature = 0f }, TestContext.Current.CancellationToken);

        Assert.Equal("hello back", response.Text);
        var sent = Assert.Single(_http.Requests);
        Assert.Equal("Bearer sk-repl-key", sent.Headers.Authorization?.ToString());
        Assert.Equal(new Uri("https://api.deepseek.com/v1/chat/completions"), sent.RequestUri);
        Assert.Equal(TimeSpan.FromSeconds(600), _http.Clients["DeepSeekLlmProvider"].Timeout);
    }

    [Fact]
    public void The_three_surfaces_share_one_provider()
    {
        using var host = Repl(
            ("Llm:BaseUrl", "https://api.deepseek.com/v1"),
            ("Llm:ApiKey", "sk-repl-key"));

        var provider = host.Services.GetRequiredService<ILlmProvider>();
        var basic = Assert.IsType<Orkeon.Infrastructure.LLMs.LlmProviderAdapter>(
            host.Services.GetRequiredService<Orkeon.Application.Interfaces.Ports.IBasicLlmProvider>());

        Assert.Same(provider, basic.UnderlyingProvider);
        Assert.Equal("sk-repl-key", provider.BaseConfig?.ApiKey);
    }

    [Fact]
    public async Task Without_an_Llm_section_the_repl_runs_on_the_echo_provider_like_orkeon_run()
    {
        using var host = Repl();

        var response = await host.Services.GetRequiredService<IChatClient>()
            .GetResponseAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("hello", response.Text);
        Assert.Empty(_http.Requests);
        Assert.Equal("undefined", host.Services.GetRequiredService<ILlmProvider>().Name);
    }
}
