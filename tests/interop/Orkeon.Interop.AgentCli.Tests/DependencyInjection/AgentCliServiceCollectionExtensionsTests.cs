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
    public async Task ShouldRefuseAtStart_WhenEngineIsUnknown()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Llm:AgentCli:Engine"] = "codex" })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOrkeonAgentCli();
        await using var provider = services.BuildServiceProvider();

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        var failure = Assert.Single(error.Failures);
        Assert.Equal("Llm:AgentCli:Engine is not an engine of the agent-cli provider: write claude, the only one.", failure);
        Assert.DoesNotContain("codex", failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldStart_WhenEngineIsLeftOut()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddOrkeonAgentCli();
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public async Task ShouldRefuse_WhenEngineIsUnknown()
    {
        await using var module = new AgentCliProviderModule(new AgentCliOptions { Engine = "codex" });

        var error = Assert.Throws<NotSupportedException>(() => module.Create(LlmConfig.OnProfile(),
            new LlmProviderModuleContext { LoggerFactory = NullLoggerFactory.Instance, FileSystem = new FakeFileSystemService() }));

        Assert.Contains("Llm:AgentCli:Engine", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("codex", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("MaxProcesses", "0", "takes a whole number of 1 or more")]
    [InlineData("MaxProcesses", "-3", "takes a whole number of 1 or more")]
    [InlineData("MaxToolResultCharacters", "999", "takes a whole number of 1000 or more")]
    [InlineData("AutocompactTokens", "0", "takes a whole number of 1 or more")]
    [InlineData("Command", " ", "takes the CLI's executable")]
    [InlineData("Command", "C:\\npm\\claude.cmd", "not a .cmd or .bat script")]
    [InlineData("Command", "claude.BAT", "not a .cmd or .bat script")]
    public async Task ShouldRefuseAtStart_WhenOptionCannotRun(string key, string value, string expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Llm:AgentCli:" + key] = value })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOrkeonAgentCli();
        await using var provider = services.BuildServiceProvider();

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        var failure = Assert.Single(error.Failures);
        Assert.StartsWith("Llm:AgentCli:" + key + " ", failure, StringComparison.Ordinal);
        Assert.Contains(expected, failure, StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(value))
            Assert.DoesNotContain(value, failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldRefuse_WhenModuleIsGivenNoProcess()
    {
        await using var module = new AgentCliProviderModule(new AgentCliOptions { MaxProcesses = 0 });

        var error = Assert.Throws<NotSupportedException>(() => module.Create(LlmConfig.OnProfile(),
            new LlmProviderModuleContext { LoggerFactory = NullLoggerFactory.Instance, FileSystem = new FakeFileSystemService() }));

        Assert.StartsWith("Llm:AgentCli:MaxProcesses ", error.Message, StringComparison.Ordinal);
    }
}
