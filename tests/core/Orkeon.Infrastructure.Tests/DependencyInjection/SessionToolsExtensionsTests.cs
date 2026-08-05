using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Infrastructure.Tests.DependencyInjection;

/// <summary>
/// <c>AddOrkeonSessionTools</c> must seed the session buffer with the model the runtime
/// will actually call, so <c>session_store get_metadata</c> reports it.
/// </summary>
/// <remarks>
/// It did not: the buffer was registered as <c>new InMemorySessionBufferService()</c> and the
/// constructor's optional <c>model</c> was never passed, so <c>SessionMetadata.Model</c> was
/// null in every host. A script has no other way to learn the active model, so a coding
/// agent's <c>/model</c> printed "(unknown)" for a perfectly well configured provider —
/// indistinguishable from having no provider at all, which is the case an operator actually
/// needs to spot.
/// </remarks>
public sealed class SessionToolsExtensionsTests
{
    [Fact]
    public void SessionBuffer_ReportsTheConfiguredModel()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ILlmProvider>(new StubLlmProvider { BaseConfig = LlmConfig.Create("boot-model") });

        services.AddOrkeonSessionTools();

        using var sp = services.BuildServiceProvider();
        Assert.Equal("boot-model", sp.GetRequiredService<ISessionBufferService>().GetMetadata().Model);
    }

    [Fact]
    public void SessionBuffer_WorksWithNoProviderAtAll()
    {
        // A host with no ILlmProvider must still get a buffer; the model is simply unknown,
        // exactly as before this registration learned to ask.
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddOrkeonSessionTools();

        using var sp = services.BuildServiceProvider();
        var meta = sp.GetRequiredService<ISessionBufferService>().GetMetadata();
        Assert.Null(meta.Model);
        Assert.False(string.IsNullOrWhiteSpace(meta.SessionId));
    }

    [Fact]
    public void SessionBuffer_ToleratesAProviderWithoutABaseConfig()
    {
        // ILlmProvider.BaseConfig is a default interface member returning null; a third-party
        // provider that never overrides it must not fail the buffer's construction.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ILlmProvider>(new StubLlmProvider());

        services.AddOrkeonSessionTools();

        using var sp = services.BuildServiceProvider();
        Assert.Null(sp.GetRequiredService<ISessionBufferService>().GetMetadata().Model);
    }

    [Fact]
    public void SessionBuffer_IsRegisteredOnce_AndTheRegistrationIsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ILlmProvider>(new StubLlmProvider { BaseConfig = LlmConfig.Create("boot-model") });

        services.AddOrkeonSessionTools();
        services.AddOrkeonSessionTools();

        using var sp = services.BuildServiceProvider();
        Assert.Single(services.Where(d => d.ServiceType == typeof(ISessionBufferService)));
        Assert.Same(
            sp.GetRequiredService<ISessionBufferService>(),
            sp.GetRequiredService<ISessionBufferService>());
    }
}
