using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Plugins;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.ConsoleApp.Tests.Fakes;

/// <summary>
/// A plugin contributing one tool, <c>plugin_echo</c>. The test assembly that holds it doubles as the plugin
/// assembly: <see cref="DependencyInjection.ReplMcpAndPluginsTests"/> copies it into a plugins folder the
/// settings name, and the REPL loads it through the real pipeline.
/// </summary>
public sealed class FakeEchoPlugin : IOrkeonPlugin
{
    /// <summary>The name of the tool the plugin registers.</summary>
    public const string ToolName = "plugin_echo";

    public string Name => "fake-echo";

    public string Version => "1.0.0";

    public void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IBaseTool>(new StubBaseTool(ToolName)
            .RespondWith(request => new ToolCallResponse(true, request.Parameters["text"], null)));
}
