using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Orkeon.Constants.FileSystem;

namespace Orkeon.Hosting.Aspire;

/// <summary>
/// Adds Orkeon processes to a .NET Aspire AppHost.
/// <para>
/// Both resources are the shipped executables (<c>orkeon-host</c>, the service daemon;
/// <c>orkeon</c>, the CLI) launched with the settings, mounts and environment an
/// operator would pass by hand -- the AppHost only describes them. What the dashboard
/// shows comes for free: Aspire injects <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> into the
/// process, the runners honour it, and every <c>invoke_agent</c>, <c>chat</c> and
/// <c>execute_tool</c> span (gen_ai.* conventions), every metric and every structured
/// log line reaches the dashboard without a line of configuration.
/// </para>
/// </summary>
public static class OrkeonResourceBuilderExtensions
{
    /// <summary>The environment prefix the Orkeon runners read configuration from (<c>ORKEON_Llm__Model</c> ...).</summary>
    public const string EnvironmentPrefix = "ORKEON_";

    /// <summary>
    /// Adds the <c>orkeon-host</c> service daemon: it registers the crews of its settings
    /// file and serves them long-running (chat gateway included).
    /// </summary>
    /// <param name="builder">The AppHost builder.</param>
    /// <param name="name">The resource name shown in the dashboard.</param>
    /// <param name="settingsPath">The daemon's <c>--settings</c> file; the daemon's own default (<c>./appsettings.json</c>) when null.</param>
    /// <param name="workingDirectory">The working directory; the AppHost's when null.</param>
    /// <param name="command">The executable; <c>orkeon-host</c> on the PATH by default.</param>
    public static IResourceBuilder<OrkeonHostResource> AddOrkeonHost(
        this IDistributedApplicationBuilder builder,
        string name,
        string? settingsPath = null,
        string? workingDirectory = null,
        string command = "orkeon-host")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var resource = new OrkeonHostResource(name, command, workingDirectory ?? builder.AppHostDirectory);
        var resourceBuilder = builder.AddResource(resource)
            .WithArgs(context =>
            {
                if (!string.IsNullOrWhiteSpace(settingsPath))
                {
                    context.Args.Add("--settings");
                    context.Args.Add(settingsPath);
                }
                context.Args.Add("--allow-external-mounts");
            })
            .WithOtlpExporter();
        return resourceBuilder;
    }

    /// <summary>
    /// Adds one crew run: <c>orkeon run &lt;crew&gt;</c>, with an <c>/output</c> mount so the
    /// crew's files land somewhere the operator can see. The process runs to completion;
    /// the dashboard keeps its traces and logs.
    /// </summary>
    /// <param name="builder">The AppHost builder.</param>
    /// <param name="name">The resource name shown in the dashboard.</param>
    /// <param name="crewPath">The crew to run: a <c>config.yaml</c>, a <c>.ork.ts</c> script or a crew directory.</param>
    /// <param name="outputDirectory">The host directory mounted read-write as <c>/output</c>; <c>&lt;AppHost&gt;/out</c> when null.</param>
    /// <param name="settingsPath">The run's <c>--settings</c> file; the settings chain of the CLI when null.</param>
    /// <param name="command">The executable; <c>orkeon</c> on the PATH by default.</param>
    [Orkeon.Compliance.Vfs.SuppressVfsCompliance("OUT-OF-SCOPE: an AppHost composes host paths for the processes it launches; the mount spec built here is what creates the runner's virtual root")]
    public static IResourceBuilder<OrkeonCrewRunResource> AddOrkeonCrewRun(
        this IDistributedApplicationBuilder builder,
        string name,
        string crewPath,
        string? outputDirectory = null,
        string? settingsPath = null,
        string command = "orkeon")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(crewPath);
        var output = System.IO.Path.GetFullPath(outputDirectory ?? System.IO.Path.Combine(builder.AppHostDirectory, "out"));
        // The runner refuses a mount whose source does not exist (a typo must not create a
        // directory); an AppHost that names the directory owns it, so it creates it.
        System.IO.Directory.CreateDirectory(output);
        var resource = new OrkeonCrewRunResource(name, command, builder.AppHostDirectory, crewPath, output);
        return builder.AddResource(resource)
            .WithArgs(context =>
            {
                context.Args.Add("run");
                context.Args.Add(crewPath);
                context.Args.Add("--mount");
                context.Args.Add($"{output}:{RunnerVirtualRoots.Output}:rw");
                context.Args.Add("--allow-external-mounts");
                if (!string.IsNullOrWhiteSpace(settingsPath))
                {
                    context.Args.Add("--settings");
                    context.Args.Add(settingsPath);
                }
            })
            .WithOtlpExporter();
    }

    /// <summary>
    /// Sets an Orkeon configuration key for the resource through the environment the
    /// runners read (<c>ORKEON_</c> prefix, <c>__</c> for <c>:</c>): <c>WithOrkeonSetting("Llm:Model", "llama3.2:1b")</c>.
    /// </summary>
    public static IResourceBuilder<T> WithOrkeonSetting<T>(this IResourceBuilder<T> builder, string key, string value)
        where T : IResourceWithEnvironment
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return builder.WithEnvironment(EnvironmentPrefix + key.Replace(":", "__", StringComparison.Ordinal), value);
    }

    /// <summary>
    /// Points the resource at a model: <c>Llm:BaseUrl</c> and <c>Llm:Model</c>, the two keys
    /// from which the runner infers the provider (Ollama on <c>:11434</c>, Docker Model
    /// Runner on <c>:12434</c>, any OpenAI-compatible endpoint otherwise).
    /// </summary>
    public static IResourceBuilder<T> WithOrkeonModel<T>(this IResourceBuilder<T> builder, Uri baseUrl, string model, string? apiKey = null)
        where T : IResourceWithEnvironment
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(baseUrl);
        builder.WithOrkeonSetting("Llm:BaseUrl", baseUrl.ToString().TrimEnd('/')).WithOrkeonSetting("Llm:Model", model);
        if (!string.IsNullOrEmpty(apiKey))
            builder.WithOrkeonSetting("Llm:ApiKey", apiKey);
        return builder;
    }
}

/// <summary>The <c>orkeon-host</c> daemon as an Aspire resource.</summary>
public sealed class OrkeonHostResource(string name, string command, string workingDirectory)
    : ExecutableResource(name, command, workingDirectory);

/// <summary>One <c>orkeon run</c> as an Aspire resource.</summary>
public sealed class OrkeonCrewRunResource(string name, string command, string workingDirectory, string crewPath, string outputDirectory)
    : ExecutableResource(name, command, workingDirectory)
{
    /// <summary>The crew the run executes.</summary>
    public string CrewPath { get; } = crewPath;

    /// <summary>The host directory mounted as <c>/output</c>.</summary>
    public string OutputDirectory { get; } = outputDirectory;
}
