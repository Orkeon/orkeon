using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Tools;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// GAP-25 — the runner host every <c>orkeon run</c> builds, validated the way a Development
/// host validates it: <c>ValidateOnBuild</c> (every registration can be constructed, no
/// singleton captures a scoped service) and <c>ValidateScopes</c> (nothing resolves a scoped
/// service from the root provider). The load and the kickoff run in a scope since GAP-25
/// (<see cref="RunnerExecutionScopeTests"/>); what the runners still read from the root — the
/// built-in tools a <c>.ork.ts</c> crew is handed, the tool registry, the file system, the LLM
/// ports — must therefore resolve there, transients included: a transient tool with a scoped
/// dependency would make every scripted crew fail under validation. Joins
/// <see cref="ConsoleSerialCollection"/> because the host warns on the process-global stderr.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerHostScopeValidationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-hosting-validation-" + Guid.NewGuid().ToString("N"));

    public RunnerHostScopeValidationTests()
    {
        Directory.CreateDirectory(_root);
        // Disabling RaggableTree keeps the on-device embedding model (ONNX) out of the host.
        File.WriteAllText(Path.Combine(_root, "appsettings.json"), "{ \"RaggableTree\": { \"Enabled\": false } }");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best-effort temp cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort temp cleanup */ }
    }

    [Fact]
    public void The_runner_host_builds_with_scope_validation_on_and_what_the_runners_read_from_its_root_resolves()
    {
        using var host = RunnerHost.Build(
            Path.Combine(_root, "appsettings.json"),
            new RunnerMountPlan { InternalMounts = [$"{FileSystemMount.Quote(_root)}:{RunnerVirtualRoots.Crew}:ro"] },
            configureBuilder: builder => builder.UseDefaultServiceProvider(options =>
            {
                options.ValidateScopes = true;
                options.ValidateOnBuild = true;
            }));
        var root = host.Services;

        // The validation is on: a scoped service asked of the root provider is refused.
        Assert.Throws<InvalidOperationException>(() => root.GetRequiredService<ICrewOrchestrationService>());

        // What RunnerExecution reads from the root: the .ork.ts load (built-in tools, LLM
        // ports, script tools registered in the registry), --list-tools, the crew loader's VFS.
        Assert.NotEmpty(root.GetServices<IBaseTool>());
        Assert.NotNull(root.GetRequiredService<IToolRegistry>());
        Assert.NotNull(root.GetRequiredService<IFileSystemService>());
        Assert.NotNull(root.GetService<ILlmProvider>());
        Assert.NotNull(root.GetService<IToolInvocationPipeline>());
        Assert.NotNull(root.GetService<ILlmProfileRegistry>());
        _ = root.GetService<IPermissionGate>();
        _ = root.GetService<ILlmDeltaSink>();
    }
}
