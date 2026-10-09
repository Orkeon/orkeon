using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orkeon.Infrastructure.Checkpointing;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Rag.DependencyInjection;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// GAP-40, decision 2 — a C# host that starts (<c>StartAsync</c>) refuses the values and the names its
/// Orkeon registrations bind, as the shipped hosts do: every section is registered with
/// <c>ValidateOnStart</c>, which <see cref="IHost.StartAsync"/> runs through
/// <c>IStartupValidator</c>. They used to surface at the first service that read them. The keys of a
/// C# host's sections stay its own: their walk belongs to the shipped hosts. Here, because the generic
/// host lives with this project.
/// </summary>
public sealed class CSharpHostStartValidationTests
{
    private static IHost Host(Action<IServiceCollection, IConfiguration> register, params (string Key, string Value)[] values)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)));
        register(builder.Services, builder.Configuration);
        return builder.Build();
    }

    private static async Task<string> RefusalAsync(IHost host)
    {
        var error = await Assert.ThrowsAnyAsync<Exception>(() => host.StartAsync(TestContext.Current.CancellationToken));
        return error is AggregateException aggregate
            ? string.Join(" | ", aggregate.Flatten().InnerExceptions.Select(inner => inner.Message))
            : error.Message;
    }

    [Fact]
    public async Task A_value_of_the_infrastructure_the_binder_cannot_convert_fails_the_start()
    {
        using var host = Host((services, _) => services.AddOrkeonInfrastructure(), ("RateLimiting:GlobalRequestsPerMinute", "sixty"));

        Assert.Contains("RateLimiting:GlobalRequestsPerMinute", await RefusalAsync(host), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_checkpointing_switch_that_is_no_boolean_fails_the_start()
    {
        using var host = Host((services, configuration) => services.AddOrkeonPostgresCheckpointing(configuration),
            ("Orkeon:Checkpointing:AutoMigrate", "maybe"));

        Assert.Contains("Orkeon:Checkpointing:AutoMigrate", await RefusalAsync(host), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rag_profile_reranking_with_onnx_fails_the_start_of_a_host_without_it()
    {
        using var host = Host(
            (services, configuration) =>
            {
                services.AddOrkeonInfrastructure();
                services.AddOrkeonRag(configuration);
            },
            ("Orkeon:Rag:Profile", "balanced"));

        var refusal = await RefusalAsync(host);

        Assert.Contains("onnx", refusal, StringComparison.Ordinal);
        Assert.Contains("Orkeon:Rag:Rerank:Kind", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_memory_provider_no_factory_serves_fails_the_start()
    {
        using var host = Host((services, _) => services.AddOrkeonInfrastructure(), ("Memory:Provider", "redsi"));

        Assert.Contains("Memory:Provider", await RefusalAsync(host), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Readable_settings_start_the_host()
    {
        using var host = Host(
            (services, configuration) =>
            {
                services.AddOrkeonInfrastructure();
                services.AddOrkeonRag(configuration);
            },
            ("RateLimiting:GlobalRequestsPerMinute", "60"),
            ("Orkeon:Rag:Profile", "fast"));

        await host.StartAsync(TestContext.Current.CancellationToken);
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        Assert.True(lifetime.ApplicationStarted.IsCancellationRequested);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }
}
