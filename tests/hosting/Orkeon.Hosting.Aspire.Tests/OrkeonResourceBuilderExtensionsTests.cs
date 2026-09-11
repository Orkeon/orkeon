using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Hosting.Aspire;

namespace Orkeon.Hosting.Aspire.Tests;

/// <summary>
/// The AppHost describes processes; these tests read the description back (arguments and
/// environment, evaluated the way Aspire does before a launch) without launching anything.
/// </summary>
public sealed class OrkeonResourceBuilderExtensionsTests
{
    private static IDistributedApplicationBuilder NewBuilder()
    {
        var options = new DistributedApplicationOptions { DisableDashboard = true, ProjectDirectory = Path.GetTempPath() };
        return DistributedApplication.CreateBuilder(options);
    }

    // What Aspire would launch: arguments and environment evaluated for a Run, no process started.
    private static async Task<(IReadOnlyList<string> Args, IReadOnlyDictionary<string, string> Env)> EvaluateAsync(DistributedApplication app, IResource resource)
    {
        var context = app.Services.GetRequiredService<DistributedApplicationExecutionContext>();
        var result = await ExecutionConfigurationBuilder.Create(resource)
            .WithArgumentsConfig()
            .WithEnvironmentVariablesConfig()
            .BuildAsync(context, NullLogger.Instance, TestContext.Current.CancellationToken);
        if (result.Exception is not null) throw result.Exception;
        return (result.Arguments.Select(a => a.Value).ToList(), result.EnvironmentVariables.ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    [Fact]
    public async Task AddOrkeonCrewRun_describes_an_orkeon_run_with_an_output_mount_and_the_otlp_exporter()
    {
        var builder = NewBuilder();
        var output = Path.Combine(Path.GetTempPath(), "orkeon-aspire-tests", Guid.NewGuid().ToString("N"));

        var crew = builder.AddOrkeonCrewRun("quickstart", "crews/quickstart.yaml", output, settingsPath: "settings.json", command: "orkeon-cli")
            .WithOrkeonModel(new Uri("http://localhost:11434/"), "llama3.2:1b");
        using var app = builder.Build();

        var resource = crew.Resource;
        Assert.Equal("orkeon-cli", resource.Command);
        Assert.Equal("crews/quickstart.yaml", resource.CrewPath);
        Assert.True(Directory.Exists(resource.OutputDirectory));

        var (args, env) = await EvaluateAsync(app, resource);
        Assert.Equal(["run", "crews/quickstart.yaml", "--mount", $"{resource.OutputDirectory}:/output:rw", "--allow-external-mounts", "--settings", "settings.json"], args);
        Assert.Equal("http://localhost:11434", env["ORKEON_Llm__BaseUrl"]);
        Assert.Equal("llama3.2:1b", env["ORKEON_Llm__Model"]);
        Assert.True(env.ContainsKey("OTEL_EXPORTER_OTLP_ENDPOINT"), "WithOtlpExporter must hand the runner the dashboard's OTLP endpoint");
        Assert.True(env.ContainsKey("OTEL_SERVICE_NAME"));
    }

    [Fact]
    public async Task AddOrkeonHost_describes_the_daemon_with_its_settings_file()
    {
        var builder = NewBuilder();

        var host = builder.AddOrkeonHost("orkeon-host", settingsPath: "host.appsettings.json")
            .WithOrkeonSetting("Host:RunTimeout", "00:10:00");
        using var app = builder.Build();

        Assert.Equal("orkeon-host", host.Resource.Command);
        var (args, env) = await EvaluateAsync(app, host.Resource);
        Assert.Equal(["--settings", "host.appsettings.json", "--allow-external-mounts"], args);
        Assert.Equal("00:10:00", env["ORKEON_Host__RunTimeout"]);
        Assert.True(env.ContainsKey("OTEL_EXPORTER_OTLP_ENDPOINT"));
    }

    [Fact]
    public async Task Without_a_settings_file_the_daemon_gets_only_the_mount_switch()
    {
        var builder = NewBuilder();
        var host = builder.AddOrkeonHost("daemon");
        using var app = builder.Build();

        var (args, _) = await EvaluateAsync(app, host.Resource);
        Assert.Equal(["--allow-external-mounts"], args);
    }
}
