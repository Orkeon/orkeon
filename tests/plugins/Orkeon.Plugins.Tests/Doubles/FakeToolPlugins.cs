using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Runtime.CompilerServices;
using Orkeon.Domain.Tools;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;
using ToolSchema = Orkeon.Domain.Tools.Protocol.ToolSchema;
using Orkeon.Plugins;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Plugins.Tests.Doubles;

// The host services below are BCL types on purpose: the test assembly is loaded a second time in the plugin's
// load context, where a type declared here would not be the host's type.

/// <summary>Marker registered by <see cref="FakeMixedPlugin"/>.</summary>
public sealed class MixedPluginMarker;

/// <summary>Marker registered by <see cref="FakeThrowingConfigurePlugin"/> before it throws.</summary>
public sealed class PartialPluginMarker;

/// <summary>A tool that takes a service only the host registers, late (a build counter).</summary>
public sealed class FakeDependentTool : IBaseTool
{
    /// <summary>Name of the tool.</summary>
    public const string ToolName = "dependent.tool";

    private readonly StubBaseTool _inner = new(ToolName);

    /// <summary>Counts, in <paramref name="builds"/> (registered by the host, after the plugins), how often a tool is built.</summary>
    public FakeDependentTool(StrongBox<int> builds)
    {
        builds.Value++;
    }

    public string Name => _inner.Name;

    public string Description => _inner.Description;

    public ToolSchema Schema => _inner.Schema;

    public Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken cancellationToken = default)
        => _inner.CallAsync(request, cancellationToken);

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
        => _inner.ExecuteAsync(input, cancellationToken);

    public bool ValidateInput(string input) => _inner.ValidateInput(input);
}

/// <summary>A tool whose constructor fails.</summary>
public sealed class FakeThrowingTool : IBaseTool
{
    public FakeThrowingTool()
    {
        throw new IOException("the tool cannot start");
    }

    public string Name => "throwing.tool";

    public string Description => string.Empty;

    public ToolSchema Schema => throw new NotSupportedException();

    public Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public bool ValidateInput(string input) => false;
}

/// <summary>Registers its tool by type.</summary>
public sealed class FakeToolTypePlugin : IOrkeonPlugin
{
    public string Name => "fake-tool-type";

    public string Version => "1.0.0";

    public void ConfigureServices(IServiceCollection services) => services.AddSingleton<IBaseTool, FakeDependentTool>();
}

/// <summary>Registers its tool through a factory.</summary>
public sealed class FakeToolFactoryPlugin : IOrkeonPlugin
{
    public string Name => "fake-tool-factory";

    public string Version => "1.0.0";

    public void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IBaseTool>(sp => new FakeDependentTool(sp.GetRequiredService<StrongBox<int>>()));
}

/// <summary>Registers its tool under a key.</summary>
public sealed class FakeKeyedToolPlugin : IOrkeonPlugin
{
    /// <summary>The key of the registration.</summary>
    public const string Key = "k";

    public string Name => "fake-tool-keyed";

    public string Version => "1.0.0";

    public void ConfigureServices(IServiceCollection services)
        => services.AddKeyedSingleton<IBaseTool>(Key, (sp, _) => new FakeDependentTool(sp.GetRequiredService<StrongBox<int>>()));
}

/// <summary>Registers a tool whose constructor throws an <see cref="IOException"/>.</summary>
public sealed class FakeThrowingToolPlugin : IOrkeonPlugin
{
    public string Name => "fake-tool-throwing";

    public string Version => "1.0.0";

    public void ConfigureServices(IServiceCollection services) => services.AddSingleton<IBaseTool, FakeThrowingTool>();
}

/// <summary>Registers a marker and a tool instance.</summary>
public sealed class FakeMixedPlugin : IOrkeonPlugin
{
    public string Name => "fake-mixed";

    public string Version => "1.0.0";

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<MixedPluginMarker>();
        services.AddSingleton<IBaseTool>(new StubBaseTool("mixed.tool"));
    }
}

/// <summary>Removes a host registration (a <see cref="StrongBox{T}"/> of string), then registers a tool instance.</summary>
public sealed class FakeReplacingPlugin : IOrkeonPlugin
{
    public string Name => "fake-replacing";

    public string Version => "1.0.0";

    public void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<StrongBox<string>>();
        services.AddSingleton<IBaseTool>(new StubBaseTool("replacing.tool"));
    }
}

/// <summary>Registers a marker, then fails.</summary>
public sealed class FakeThrowingConfigurePlugin : IOrkeonPlugin
{
    public string Name => "fake-throwing-configure";

    public string Version => "1.0.0";

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<PartialPluginMarker>();
        throw new InvalidOperationException("ConfigureServices failed");
    }
}
