using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Commands.Scripting.DependencyInjection;
using Orkeon.Scripting.Toolchain;

namespace Orkeon.Cli.Commands.Scripting.Tests.Configuration;

/// <summary>
/// Guards the <see cref="IScriptTranspiler"/> registration in
/// <see cref="ScriptingCliServiceCollectionExtensions.AddScriptCommands"/> against a
/// regression where the factory resolved <c>GetServices&lt;IScriptTranspiler&gt;()</c> from
/// inside its own body. Because the factory IS the (Try-added) registration for the type,
/// that self-lookup re-invoked the factory forever and hung host boot during DI resolution.
/// </summary>
public sealed class TranspilerRegistrationTests
{
    [Fact]
    public void Resolving_transpiler_with_esbuild_enabled_does_not_recurse()
    {
        var services = new ServiceCollection();
        services.AddScriptCommands(configure: cfg => cfg.EsbuildTranspile = true);

        using var provider = services.BuildServiceProvider();

        // Before the fix this call never returned (infinite self-recursion). A successful,
        // prompt resolution is the assertion; the concrete type confirms the enabled branch.
        var transpiler = provider.GetRequiredService<IScriptTranspiler>();

        Assert.IsType<EsbuildTranspiler>(transpiler);
    }

    [Fact]
    public void Resolving_transpiler_with_esbuild_disabled_returns_passthrough()
    {
        var services = new ServiceCollection();
        services.AddScriptCommands(configure: cfg => cfg.EsbuildTranspile = false);

        using var provider = services.BuildServiceProvider();

        var transpiler = provider.GetRequiredService<IScriptTranspiler>();

        Assert.Same(PassThroughTranspiler.Instance, transpiler);
    }

    [Fact]
    public void Host_registered_transpiler_takes_precedence()
    {
        var custom = new PassThroughTranspiler();
        var services = new ServiceCollection();
        // A host that registers its own transpiler first wins outright: TryAddSingleton in
        // AddScriptCommands becomes a no-op, so no self-referential factory is ever added.
        services.AddSingleton<IScriptTranspiler>(custom);
        services.AddScriptCommands(configure: cfg => cfg.EsbuildTranspile = true);

        using var provider = services.BuildServiceProvider();

        Assert.Same(custom, provider.GetRequiredService<IScriptTranspiler>());
    }
}
