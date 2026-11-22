using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Interop.AgentCli.DependencyInjection;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Interop.AgentCli.Tests.DependencyInjection;

public sealed class AgentCliServiceCollectionExtensionsTests
{
    [Fact]
    public async Task ShouldRegisterOneModule_WhenAddedTwice()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Llm:AgentCli:MaxProcesses"] = "2", ["Llm:AgentCli:Command"] = "/opt/claude" })
            .Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration);

        services.AddOrkeonAgentCli().AddOrkeonAgentCli();
        await using var provider = services.BuildServiceProvider();

        var module = Assert.Single(provider.GetServices<ILlmProviderModule>());
        Assert.Equal("agent-cli", module.Key);
        var options = provider.GetRequiredService<IOptions<AgentCliOptions>>().Value;
        Assert.Equal(2, options.MaxProcesses);
        Assert.Equal("/opt/claude", options.Command);
    }

    [Fact]
    public async Task ShouldBuildProviderNamedAgentCli_WhenModuleCreates()
    {
        await using var module = new AgentCliProviderModule(new AgentCliOptions());

        var provider = module.Create(LlmConfig.Create("haiku"),
            new LlmProviderModuleContext { LoggerFactory = NullLoggerFactory.Instance, FileSystem = new FakeFileSystemService() });

        Assert.Equal("agent-cli", provider.Name);
        Assert.False(provider.Capabilities.RunsOwnTools);
        Assert.Equal("haiku", provider.BaseConfig!.Model);
    }

    [Fact]
    public async Task ShouldRefuse_WhenEngineIsUnknown()
    {
        await using var module = new AgentCliProviderModule(new AgentCliOptions { Engine = "codex" });

        var error = Assert.Throws<NotSupportedException>(() => module.Create(LlmConfig.OnProfile(),
            new LlmProviderModuleContext { LoggerFactory = NullLoggerFactory.Instance, FileSystem = new FakeFileSystemService() }));

        Assert.Contains("codex", error.Message, StringComparison.Ordinal);
    }
}
