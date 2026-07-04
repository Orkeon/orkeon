using Microsoft.Extensions.DependencyInjection;
using Orkeon.Analysis.Abstractions.DependencyInjection;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Embeddings.Local.DependencyInjection;

namespace Orkeon.Tools.Embeddings.Local.Tests;

/// <summary>
/// Registration tests for <see cref="ServiceCollectionExtensions.AddOrkeonLocalEmbeddings"/>.
/// We inspect the produced <see cref="ServiceDescriptor"/>s rather than resolving the
/// services: resolving <see cref="IEmbeddingProvider"/> / <see cref="IBaseTool"/> would boot the
/// real ONNX session (the factory is lazy, so registration itself stays ONNX-free).
/// </summary>
public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddOrkeonLocalEmbeddings_NullServices_Throws()
    {
        IServiceCollection services = null!;
        Assert.Throws<ArgumentNullException>(() => services.AddOrkeonLocalEmbeddings());
    }

    [Fact]
    public void AddOrkeonLocalEmbeddings_ReturnsSameInstance_ForChaining()
    {
        var services = new ServiceCollection();

        var returned = services.AddOrkeonLocalEmbeddings();

        Assert.Same(services, returned);
    }

    [Fact]
    public void AddOrkeonLocalEmbeddings_RegistersAllThreeServices_AsSingletons()
    {
        var services = new ServiceCollection();

        services.AddOrkeonLocalEmbeddings();

        var options = services.Single(d => d.ServiceType == typeof(LocalEmbeddingOptions));
        var provider = services.Single(d => d.ServiceType == typeof(IEmbeddingProvider));
        var tool = services.Single(d => d.ServiceType == typeof(IBaseTool));

        Assert.Equal(ServiceLifetime.Singleton, options.Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, provider.Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, tool.Lifetime);
        Assert.Equal(typeof(LocalEmbedTool), tool.ImplementationType);
    }

    [Fact]
    public void AddOrkeonLocalEmbeddings_NullOptions_RegistersDefaultOptionsInstance()
    {
        var services = new ServiceCollection();

        services.AddOrkeonLocalEmbeddings(options: null);

        var descriptor = services.Single(d => d.ServiceType == typeof(LocalEmbeddingOptions));
        var instance = Assert.IsType<LocalEmbeddingOptions>(descriptor.ImplementationInstance);
        // Documented defaults.
        Assert.Equal(2000, instance.MaxTextChars);
        Assert.Null(instance.ModelPath);
    }

    [Fact]
    public void AddOrkeonLocalEmbeddings_SuppliedOptions_AreRegisteredVerbatim()
    {
        var services = new ServiceCollection();
        var opts = new LocalEmbeddingOptions { MaxTextChars = 42, MaxConcurrency = 2 };

        services.AddOrkeonLocalEmbeddings(opts);

        var descriptor = services.Single(d => d.ServiceType == typeof(LocalEmbeddingOptions));
        Assert.Same(opts, descriptor.ImplementationInstance);
    }

    [Fact]
    public void AddOrkeonLocalEmbeddings_OptionsAndProvider_AreIdempotent_ViaTryAdd()
    {
        var services = new ServiceCollection();
        var first = new LocalEmbeddingOptions { MaxTextChars = 11 };

        services.AddOrkeonLocalEmbeddings(first);
        services.AddOrkeonLocalEmbeddings(new LocalEmbeddingOptions { MaxTextChars = 99 });

        // TryAddSingleton: the first options registration wins.
        Assert.Single(services, d => d.ServiceType == typeof(LocalEmbeddingOptions));
        Assert.Single(services, d => d.ServiceType == typeof(IEmbeddingProvider));
        var optsDescriptor = services.Single(d => d.ServiceType == typeof(LocalEmbeddingOptions));
        Assert.Same(first, optsDescriptor.ImplementationInstance);
    }

    [Fact]
    public void AddOrkeonLocalEmbeddings_CalledTwice_RegistersTwoTools()
    {
        // The tool uses AddSingleton (not TryAdd), so two calls = two IBaseTool entries.
        var services = new ServiceCollection();

        services.AddOrkeonLocalEmbeddings();
        services.AddOrkeonLocalEmbeddings();

        Assert.Equal(2, services.Count(d => d.ServiceType == typeof(IBaseTool)));
    }

    [Fact]
    public void AddOrkeonLocalEmbeddings_ProviderFactory_IsLazy_NotInvokedAtRegistration()
    {
        // Proves registration does not boot ONNX: the provider descriptor carries a factory,
        // not a pre-built instance.
        var services = new ServiceCollection();

        services.AddOrkeonLocalEmbeddings();

        var descriptor = services.Single(d => d.ServiceType == typeof(IEmbeddingProvider));
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Null(descriptor.ImplementationInstance);
    }
}
