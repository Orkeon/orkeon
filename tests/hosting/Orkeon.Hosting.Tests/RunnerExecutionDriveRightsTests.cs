using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Infrastructure.LLMs;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// DRIVE — a crew whose drive tools need a right no ready account grants is refused at crew load,
/// before any model call, the way a crew short of a mail right is. Joins
/// <see cref="ConsoleSerialCollection"/> because it redirects the process-global console.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerExecutionDriveRightsTests : IDisposable
{
    private sealed class TestOptions : RunnerOptionsBase { }

    private readonly string _tempDir;

    public RunnerExecutionDriveRightsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-hosting-drive-rights-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "config.yaml"), """
            name: "drive-rights"
            goal: "Crew whose agent files reports"
            process: "sequential"
            agents:
              filer:
                role: "Filer"
                goal: "File the report"
                backstory: "A minimal test agent."
                tools:
                  - "drive_upload"
                maxIter: 1
            tasks:
              file_it:
                description: "Upload the report."
                expectedOutput: "Uploaded."
                agent: "filer"
            """);
        // A reading account: drive_upload needs Write, which no account grants.
        File.WriteAllText(Path.Combine(_tempDir, "appsettings.json"), """
            { "RaggableTree": { "Enabled": false },
              "Orkeon": { "Tools": { "Drive": { "Accounts": { "reader": {
                "Provider": "OneDrive", "Rights": "Read", "Auth": { "ClientId": "client-id" } } } } } } }
            """);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* best-effort temp cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort temp cleanup */ }
    }

    [Fact]
    public async Task ShouldRefuseAtCrewLoadBeforeAnyModelCall_WhenADriveToolLacksItsRight()
    {
        var llm = new CountingLlmProvider();
        var opts = new TestOptions { ConfigPath = Path.Combine(_tempDir, "config.yaml"), AllowExternalMounts = true };

        var (exit, stderr) = await CaptureAsync(() => RunnerExecution.RunOneShotAsync(
            opts, "Orkeon.Hosting.Tests",
            (_, services) => services.AddSingleton<ILlmProviderFactory>(new CountingFactory(llm)),
            TestContext.Current.CancellationToken));

        Assert.Equal(2, exit);
        Assert.Equal(0, llm.Calls);
        Assert.Contains("drive_upload needs the Write right on a ready drive account", stderr, StringComparison.Ordinal);
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
}
