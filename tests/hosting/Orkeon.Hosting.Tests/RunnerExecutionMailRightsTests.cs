using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Infrastructure.LLMs;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// A crew whose mail tools need a right no ready account grants is refused at crew load, before any
/// model call: the one-shot run exits 2 with an ERROR line, --validate fails the same way. Joins
/// <see cref="ConsoleSerialCollection"/> because it redirects the process-global console.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerExecutionMailRightsTests : IDisposable
{
    private sealed class TestOptions : RunnerOptionsBase { }

    private readonly string _tempDir;
    private readonly string _passwordVar = "ORKEON_TEST_MAIL_" + Guid.NewGuid().ToString("N");

    public RunnerExecutionMailRightsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-hosting-mail-rights-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "config.yaml"), """
            name: "mail-rights"
            goal: "Crew whose agent sends mail"
            process: "sequential"
            agents:
              mailer:
                role: "Mailer"
                goal: "Send mail"
                backstory: "A minimal test agent."
                tools:
                  - "email_send"
                maxIter: 1
            tasks:
              do_mail:
                description: "Send the mail."
                expectedOutput: "Sent."
                agent: "mailer"
            """);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(_passwordVar, null);
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* best-effort temp cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort temp cleanup */ }
    }

    private void WriteSettings(bool withAccount)
    {
        var email = withAccount
            ? $$"""
              , "Orkeon": { "Tools": { "Email": { "Accounts": { "agent": {
                  "Provider": "Custom", "Address": "agent@example.com", "Rights": "Read, Send",
                  "Incoming": { "Host": "imap.example.test" }, "Outgoing": { "Host": "smtp.example.test" },
                  "Auth": { "PasswordEnvVar": "{{_passwordVar}}" },
                  "Send": { "AllowedRecipients": [ "*@example.com" ] } } } } } }
              """
            : string.Empty;
        File.WriteAllText(
            Path.Combine(_tempDir, "appsettings.json"),
            "{ \"RaggableTree\": { \"Enabled\": false }" + email + " }");
    }

    [Fact]
    [Trait("BR", "BR-06")]
    [Trait("BR", "UC-04")]
    public async Task ShouldRefuseAtCrewLoadBeforeAnyModelCall_WhenAMailToolLacksItsRight()
    {
        WriteSettings(withAccount: false);
        var llm = new CountingLlmProvider();
        var orchestrator = new CountingOrchestrator();

        var (exit, _) = await RunAsync(services =>
        {
            services.AddSingleton<ICrewOrchestrationService>(orchestrator);
            services.AddSingleton<ILlmProviderFactory>(new CountingFactory(llm));
        });

        Assert.Equal(2, exit);
        Assert.Equal(0, llm.Calls);
        Assert.Equal(0, orchestrator.Kickoffs);
    }

    [Fact]
    [Trait("BR", "UC-03")]
    public async Task ShouldRunTheCrew_WhenTheAccountGrantsTheRights()
    {
        WriteSettings(withAccount: true);
        Environment.SetEnvironmentVariable(_passwordVar, "x");
        var orchestrator = new CountingOrchestrator();

        var (exit, stderr) = await RunAsync(services => services.AddSingleton<ICrewOrchestrationService>(orchestrator));

        Assert.True(exit == 0, stderr);
        Assert.Equal(1, orchestrator.Kickoffs);
    }

    [Fact]
    [Trait("BR", "BR-08")]
    public async Task ShouldRefuseTheSameWay_WhenValidating()
    {
        WriteSettings(withAccount: false);
        var opts = new TestOptions { ConfigPath = Path.Combine(_tempDir, "config.yaml"), AllowExternalMounts = true };

        var (exit, stderr) = await CaptureAsync(
            () => RunnerExecution.RunValidateAsync(opts, "Orkeon.Hosting.Tests"));

        Assert.NotEqual(0, exit);
        Assert.Contains("email_send", stderr, StringComparison.Ordinal);
        Assert.Contains("Send", stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "BR-06")]
    public async Task ShouldExitWithTheCrewConfigurationCode_WhenRefused()
    {
        WriteSettings(withAccount: false);

        var (exit, stderr) = await RunAsync(_ => { });

        Assert.Equal(2, exit);
        var lastLine = stderr.TrimEnd().Split('\n').Last().TrimEnd('\r');
        Assert.StartsWith("ERROR: ", lastLine, StringComparison.Ordinal);
        Assert.Contains("email_send", lastLine, StringComparison.Ordinal);
        Assert.Contains("Send", lastLine, StringComparison.Ordinal);
        Assert.Contains("Settings › E-mail", lastLine, StringComparison.Ordinal);
    }

    private Task<(int exit, string stderr)> RunAsync(Action<IServiceCollection> configure)
    {
        var opts = new TestOptions { ConfigPath = Path.Combine(_tempDir, "config.yaml"), AllowExternalMounts = true };
        return CaptureAsync(() => RunnerExecution.RunOneShotAsync(
            opts, "Orkeon.Hosting.Tests", (_, services) => configure(services), TestContext.Current.CancellationToken));
    }

    private static async Task<(int exit, string stderr)> CaptureAsync(Func<Task<int>> run)
    {
        var origOut = Console.Out;
        var origErr = Console.Error;
        using var stdout = new StringWriter(new StringBuilder());
        using var stderr = new StringWriter(new StringBuilder());
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            var exit = await run().ConfigureAwait(false);
            return (exit, stderr.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }

    private sealed class CountingFactory(CountingLlmProvider provider) : ILlmProviderFactory
    {
        public IBasicLlmProvider Create(LlmConfig config) => new LlmProviderAdapter(provider);
    }

    /// <summary>Counts the kickoffs and answers each with a success.</summary>
    private sealed class CountingOrchestrator : ICrewOrchestrationService
    {
        private int _kickoffs;

        public int Kickoffs => Volatile.Read(ref _kickoffs);

        public Task<CrewOutput> KickoffAsync(CrewId crewId, CrewInput input, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _kickoffs);
            return Task.FromResult(new CrewOutput("the answer", [], TimeSpan.FromSeconds(1), TokensUsed: null));
        }

        public Task<BatchOutput> KickoffForEachAsync(CrewId crewId, IEnumerable<CrewInput> inputs, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CrewExecutionId> KickoffAsyncNoWait(CrewId crewId, CrewInput input) =>
            throw new NotSupportedException();

        public Task<CrewExecutionStatus> GetExecutionStatusAsync(CrewExecutionId executionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<CrewExecutionEvent> KickoffStreamingAsync(CrewId crewId, CrewInput input, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
