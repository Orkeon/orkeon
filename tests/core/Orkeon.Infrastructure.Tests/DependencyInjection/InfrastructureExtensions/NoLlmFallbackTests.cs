using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.DependencyInjection.InfrastructureExtensions;

/// <summary>
/// GAP-29, decision 3: <c>AddOrkeonInfrastructure()</c> registers no model of its own. Its
/// <c>TryAdd</c> fallbacks built a second provider from an empty configuration — OpenAI's
/// endpoint, no key — whose every call answered "API key is required"; <c>orkeon-repl</c>, which
/// registered no chat client, served that one. Every shipped host registers its model now, and a
/// container that does not is told so at its first LLM resolution.
/// </summary>
public sealed class NoLlmFallbackTests
{
    [Fact]
    public void A_container_that_registers_no_model_says_so_at_its_first_resolution()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOrkeonInfrastructure();
        using var container = services.BuildServiceProvider();

        var failure = Assert.Throws<InvalidOperationException>(() => container.GetRequiredService<ILlmProvider>());
        Assert.Contains("No LLM provider is registered", failure.Message, StringComparison.Ordinal);
        Assert.Contains("AddOrkeonLlmProvider", failure.Message, StringComparison.Ordinal);
        // Nothing stands in for the host's model: an optional resolution finds none.
        Assert.Null(container.GetService<IChatClient>());
        Assert.Null(container.GetService<IBasicLlmProvider>());
    }

    [Fact]
    public void A_registered_model_is_the_one_all_three_surfaces_serve()
    {
        var model = new MockLlmProvider { Name = "host-model", BaseConfig = LlmConfig.Create("m", "sk-host") };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOrkeonLlmProvider(_ => model, LlmConfig.Create("m", "sk-host"));
        services.AddOrkeonInfrastructure();
        using var container = services.BuildServiceProvider();

        var provider = container.GetRequiredService<ILlmProvider>();
        var basic = Assert.IsType<LlmProviderAdapter>(container.GetRequiredService<IBasicLlmProvider>());

        Assert.Same(model, MeteredLlmProvider.Unwrap(provider));
        Assert.Same(provider, basic.UnderlyingProvider);
        Assert.IsType<LlmProviderToChatClientAdapter>(container.GetRequiredService<IChatClient>());
    }

    [Fact]
    public void A_host_registering_its_basic_provider_alone_gets_the_provider_under_it()
    {
        // The runners register their configured provider as IBasicLlmProvider (and a chat
        // client); the infrastructure exposes the provider under it as ILlmProvider.
        var model = new MockLlmProvider { Name = "runner-model" };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IBasicLlmProvider>(new LlmProviderAdapter(model));
        services.AddOrkeonInfrastructure();
        using var container = services.BuildServiceProvider();

        Assert.Same(model, container.GetRequiredService<ILlmProvider>());
    }
}
