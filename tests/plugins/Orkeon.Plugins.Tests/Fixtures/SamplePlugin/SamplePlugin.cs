using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Plugins;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Plugins.Tests.Fixtures;

/// <summary>
/// The sample plugin: the smallest plugin a manifest can describe. <c>Fixtures/SamplePlugin/plugin.json</c>
/// declares it (name, version, Orkeon range, entry assembly, the one tool it registers). The test assembly
/// that holds it is the entry assembly: <see cref="PluginDirectoryFixture.AddSamplePlugin"/> copies both into
/// a plugin folder and the real pipeline loads it. The <c>orkeon-plugin</c> template (FWK-26) starts from it.
/// </summary>
public sealed class SamplePlugin : IOrkeonPlugin
{
    /// <summary>The tool the plugin registers and its manifest declares.</summary>
    public const string ToolName = "sample.echo";

    public string Name => "sample-plugin";

    public string Version => "1.0.0";

    public void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IBaseTool>(new StubBaseTool(ToolName)
            .RespondWith(request => new ToolCallResponse(true, request.Parameters["text"], null)));
}
