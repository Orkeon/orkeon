using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Crew;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.FileSystem;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Application.Tests.DependencyInjection;

/// <summary>
/// FWK-20: the orchestrator <c>AddOrkeonApplication</c> builds holds the DLP interceptor of the external-output
/// channel when the host registered DLP, and none otherwise, so the streamed tool-call arguments are held only
/// where a policy asks for it.
/// </summary>
public sealed class ToolCallDeltaDlpWiringTests
{
    private sealed class FakeDlpPolicyProvider : IDlpPolicyProvider
    {
        public DlpPolicy GetPolicy(DlpChannel channel) => new() { Channel = channel, DefaultAction = DlpAction.Mask };

        public IReadOnlyList<DlpPolicy> GetAllPolicies() => [];
    }

    private static ServiceCollection Host()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddLogging();
        services.AddSingleton<IBasicLlmProvider>(new ScriptedBasicLlmProvider());
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddOrkeonApplication();
        return services;
    }

    [Fact]
    [Trait("BR", "FWK-20")]
    public void ShouldHandTheExternalOutputInterceptorToTheOrchestrator_WhenTheHostRegisteredDlp()
    {
        var services = Host();
        var interceptor = new FakeDlpInterceptor();
        services.AddSingleton<IDlpInterceptor>(interceptor);
        services.AddSingleton<IDlpPolicyProvider, FakeDlpPolicyProvider>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var orchestrator = Assert.IsType<ExecutionOrchestrator>(scope.ServiceProvider.GetRequiredService<IExecutionOrchestrator>());

        Assert.NotNull(orchestrator.ToolCallDlp);
        Assert.Same(interceptor, orchestrator.ToolCallDlp.Interceptor);
        Assert.Equal(DlpChannel.ExternalOutput, orchestrator.ToolCallDlp.Policy.Channel);
    }

    private sealed class NoopSink : ILlmDeltaSink
    {
        public void OnDelta(string delta)
        {
        }

        public void OnTurnCompleted()
        {
        }
    }

    [Fact]
    [Trait("BR", "FWK-20")]
    public void ShouldWrapTheHostsSinkWithItsDlp_WhenTheHostRegisteredDlp()
    {
        var services = Host();
        var interceptor = new FakeDlpInterceptor();
        services.AddSingleton<IDlpInterceptor>(interceptor);
        services.AddSingleton<IDlpPolicyProvider, FakeDlpPolicyProvider>();
        using var provider = services.BuildServiceProvider();

        var wrapped = ToolCallDlpDeltaSink.Wrap(new NoopSink(), provider);

        var sink = Assert.IsType<ToolCallDlpDeltaSink>(wrapped);
        Assert.Same(interceptor, sink.Dlp.Interceptor);
    }

    [Fact]
    [Trait("BR", "FWK-20")]
    public void ShouldKeepTheHostsSinkAsItIs_WhenNoDlpOrNoSink()
    {
        using var provider = Host().BuildServiceProvider();
        var sink = new NoopSink();

        Assert.Same(sink, ToolCallDlpDeltaSink.Wrap(sink, provider));
        Assert.Null(ToolCallDlpDeltaSink.Wrap(null, provider));
    }

    [Fact]
    [Trait("BR", "FWK-20")]
    public void ShouldLeaveTheOrchestratorWithoutInterceptor_WhenTheHostRegisteredNoDlp()
    {
        using var provider = Host().BuildServiceProvider();
        using var scope = provider.CreateScope();

        var orchestrator = Assert.IsType<ExecutionOrchestrator>(scope.ServiceProvider.GetRequiredService<IExecutionOrchestrator>());

        Assert.Null(orchestrator.ToolCallDlp);
    }
}
