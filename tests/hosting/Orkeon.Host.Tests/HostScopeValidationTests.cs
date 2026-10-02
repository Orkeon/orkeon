using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orkeon.Hosting;

namespace Orkeon.Host.Tests;

/// <summary>
/// GAP-25 — the daemon's container, validated the way a Development host validates it:
/// <c>ValidateOnBuild</c> (every registration can be constructed, no singleton captures a scoped
/// service) and <c>ValidateScopes</c> (nothing resolves a scoped service from the root provider).
/// The host is the one <c>Program</c> builds — the runner host plus
/// <see cref="HostServiceRegistration.AddHostServices"/> — and a hosted crew runs end to end in
/// it, on the echo provider (no <c>Llm</c> section): a scoped service reached from the root
/// anywhere on that path, the orchestrator's internals included, throws instead of silently
/// sharing one run's state with the next.
/// </summary>
public sealed class HostScopeValidationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"orkeon-host-scopes-{Guid.NewGuid():N}");

    public HostScopeValidationTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    [Fact]
    public async Task The_daemon_builds_and_runs_a_hosted_crew_with_scope_validation_on()
    {
        var crewPath = Path.Combine(_root, "crews", "support.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(crewPath)!);
        await File.WriteAllTextAsync(crewPath, """
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
            """, TestContext.Current.CancellationToken);

        // Disabling RaggableTree keeps the on-device embedding model (ONNX) out of the host.
        var settings = Path.Combine(_root, "appsettings.json");
        await File.WriteAllTextAsync(settings, JsonSerializer.Serialize(new
        {
            RaggableTree = new { Enabled = false },
            Orkeon = new { Host = new { Crews = new[] { new { Name = "support", Path = crewPath } } } },
        }), TestContext.Current.CancellationToken);

        var crews = new[] { new HostedCrewOptions { Name = "support", Path = crewPath } };
        var crewPlan = HostCrewMounts.For(crews);

        using var host = RunnerHost.Build(
            settings,
            new RunnerMountPlan { CliMounts = [.. crewPlan.Mounts], AllowExternalMounts = true },
            configureServices: (context, services) => services.AddHostServices(context.Configuration, crewPlan),
            configureBuilder: builder => builder.UseDefaultServiceProvider(options =>
            {
                options.ValidateScopes = true;
                options.ValidateOnBuild = true;
            }));

        // The validation is on: a scoped service asked of the root provider is refused.
        Assert.Throws<InvalidOperationException>(
            () => host.Services.GetRequiredService<Orkeon.Application.Interfaces.Services.ICrewOrchestrationService>());

        // Starting the host resolves its hosted services from the root first.
        Assert.NotEmpty(host.Services.GetServices<IHostedService>());

        var result = await host.Services.GetRequiredService<ICrewRunner>()
            .RunAsync("support", "hello", "test:scope-validation");

        Assert.True(result.Succeeded, $"{result.Outcome}: {result.Message}");
    }
}
