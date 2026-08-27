using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.DependencyInjection;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.FileSystem;

namespace Orkeon.Infrastructure.Tests.DependencyInjection;

/// <summary>
/// The container the runners build, validated the way the container itself knows how:
/// <c>ValidateOnBuild</c> (every registration can actually be constructed) and
/// <c>ValidateScopes</c> (no singleton captures a scoped service).
/// <para>
/// This is a whole class of defect the rest of the suite cannot see. Every other test resolves
/// the two or three services it needs, so a registration nothing happens to resolve is never
/// constructed, and a captured-scoped-dependency only misbehaves under concurrency — in
/// production, days later, as a request answered with another request's state. Neither shows up
/// as a failing assertion anywhere; the graph has to be asked as a whole.
/// </para>
/// </summary>
public sealed class ContainerLifetimeTests : IDisposable
{
    private readonly string _workspace;

    public ContainerLifetimeTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), $"orkeon-di-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    private ServiceCollection BuildRunnerServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orkeon:FileSystem:Mounts:0"] = FileSystemMount.Quote(_workspace) + ":/workspace:rw",
                ["Orkeon:Sandbox:EphemeralRoot"] = Path.Combine(_workspace, "sandbox"),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOrkeonInfrastructure(configuration);
        services.AddOrkeonApplication();
        services.AddOrkeonFileSystem(configuration);
        return services;
    }

    [Fact]
    public void The_runner_graph_builds_with_scope_validation_on()
    {
        var services = BuildRunnerServices();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        Assert.NotNull(provider);
    }

    /// <summary>
    /// The services a crew run actually pulls, resolved from a scope with validation on — the
    /// path where a singleton capturing a scoped dependency throws instead of silently sharing
    /// one execution's state with the next.
    /// </summary>
    [Fact]
    public void The_services_a_run_resolves_have_consistent_lifetimes()
    {
        var services = BuildRunnerServices();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        Assert.NotNull(sp.GetRequiredService<IFileSystemService>());
        Assert.NotNull(sp.GetRequiredService<PrivilegedFileSystemAccess>());
        Assert.NotNull(sp.GetRequiredService<FileSystemRegistry>());
        Assert.NotNull(sp.GetRequiredService<Orkeon.Application.Interfaces.Services.ICrewOrchestrationService>());
        Assert.NotNull(sp.GetRequiredService<Orkeon.Infrastructure.Crew.Strategies.SequentialProcessStrategy>());
        Assert.NotNull(sp.GetRequiredService<Orkeon.Infrastructure.Crew.Strategies.ParallelProcessStrategy>());
        Assert.NotNull(sp.GetRequiredService<Orkeon.Infrastructure.Crew.Strategies.GraphProcessStrategy>());
        Assert.NotNull(sp.GetRequiredService<Orkeon.Infrastructure.Crew.Strategies.TaskAgentSelector>());
    }

    /// <summary>
    /// The sandbox virtual root is not merely mounted — it RESOLVES, against the real
    /// <c>IPathValidator</c>, for the write that every code execution starts with.
    /// <para>
    /// Resolving a virtual path is two steps: the registry answers WHERE, then
    /// <c>IPathValidator</c> answers WHETHER. Mounting <c>/sandbox</c> in every registry
    /// fixed step one and left step two denying it — the session directory lives under the
    /// temp directory while the validator's workspace root defaults to the current one — so
    /// <c>DockerSandbox.CreateDirectoryAsync</c> and <c>ProcessIsolationSandbox</c>, whose
    /// run directory is the first thing they create, kept failing on the first
    /// agent-generated snippet. Only the message changed: "No mount found for virtual path
    /// '/sandbox/…'" became "Path is outside the allowed workspace directory".
    /// </para>
    /// <para>
    /// Every other test touching this mount stubs the validator away, which is precisely why
    /// none of them could see it. This one does not.
    /// </para>
    /// </summary>
    [Fact]
    public void The_sandbox_root_clears_the_real_path_validator()
    {
        var services = BuildRunnerServices();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        // The real one, not a double: assert that before relying on the result.
        var validator = provider.GetRequiredService<Orkeon.Domain.Tools.Security.IPathValidator>();
        Assert.IsType<Orkeon.Infrastructure.Security.PathValidator>(validator);

        var session = provider.GetRequiredService<Orkeon.Infrastructure.Sandbox.SandboxSession>();
        var privileged = provider.GetRequiredService<PrivilegedFileSystemAccess>();

        var result = privileged.FileSystem.ResolveAndValidate(
            $"{session.Mount.VirtualPath}/docker/run-1/Program.cs",
            FileAccessRights.Write | FileAccessRights.Create);

        Assert.True(result.IsAllowed, result.DenialReason);
        Assert.StartsWith(session.Root, result.ResolvedPath!, PhysicalPathContainment.Comparison);
    }

    /// <summary>
    /// With no embedding generator registered, the port resolves to the provider whose whole
    /// job is to say so — and throws at the first embed, not at container build.
    /// <para>
    /// <c>AddOrkeonVectorSearch</c> registered <c>OpenAIEmbeddingProvider</c> by type, and its
    /// constructor needs an M.E.AI generator that only a host choosing local embeddings ever
    /// registers. MS.DI throws when a registered service's dependencies cannot be resolved, so
    /// <c>GetService&lt;OpenAIEmbeddingProvider&gt;()</c> threw instead of returning null —
    /// putting both graceful fallbacks behind an exception that names
    /// <c>IEmbeddingGenerator&lt;String, Embedding&lt;Single&gt;&gt;</c>, an interface no
    /// operator has heard of, at startup rather than at first use.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Without_an_embedding_generator_the_port_says_so_at_first_use()
    {
        var services = BuildRunnerServices();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        var embedding = provider.GetRequiredService<Orkeon.Application.Interfaces.Ports.IEmbeddingProvider>();

        // Non-throwing on the diagnostic surface…
        Assert.NotNull(embedding.Name);

        // …and actionable on the one that matters.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => embedding.GetEmbeddingAsync("hello", TestContext.Current.CancellationToken));
        Assert.Contains("AddOrkeonLocalEmbeddings", ex.Message, StringComparison.Ordinal);
    }
}
