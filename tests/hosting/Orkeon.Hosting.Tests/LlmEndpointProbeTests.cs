using System.Net;
using System.Net.Sockets;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// Tests for <see cref="RunnerExecution.IsLlmEndpointReachableAsync"/> — the pre-kickoff TCP
/// probe that fails a run fast when the configured LLM endpoint is not listening, instead of
/// letting the crew run to an empty output at exit 0 (LLM providers swallow the failure behind
/// sanitized exceptions + Polly retries, so the connection-refused catch never fires).
/// </summary>
public class LlmEndpointProbeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NoUrl_IsTreatedAsReachable(string? baseUrl)
    {
        var reachable = await RunnerExecution.IsLlmEndpointReachableAsync(
            baseUrl, Timeout, TestContext.Current.CancellationToken);

        Assert.True(reachable);
    }

    [Fact]
    public async Task UnparseableUrl_IsTreatedAsReachable()
    {
        var reachable = await RunnerExecution.IsLlmEndpointReachableAsync(
            "not a url", Timeout, TestContext.Current.CancellationToken);

        Assert.True(reachable);
    }

    [Fact]
    public async Task RefusedPort_IsUnreachable()
    {
        // Reserve an ephemeral port, then release it so connections are actively refused.
        int port;
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
        }

        var reachable = await RunnerExecution.IsLlmEndpointReachableAsync(
            $"http://127.0.0.1:{port}/v1", Timeout, TestContext.Current.CancellationToken);

        Assert.False(reachable);
    }

    [Fact]
    public async Task ListeningPort_IsReachable()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var reachable = await RunnerExecution.IsLlmEndpointReachableAsync(
            $"http://127.0.0.1:{port}/v1", Timeout, TestContext.Current.CancellationToken);

        Assert.True(reachable);
    }

    [Fact]
    public async Task UnresolvableHost_IsInconclusiveAndTreatedAsReachable()
    {
        // DNS failure is inconclusive (not an active refusal) → must not block the run.
        var reachable = await RunnerExecution.IsLlmEndpointReachableAsync(
            "http://nonexistent.invalid:12434/v1", Timeout, TestContext.Current.CancellationToken);

        Assert.True(reachable);
    }
}
