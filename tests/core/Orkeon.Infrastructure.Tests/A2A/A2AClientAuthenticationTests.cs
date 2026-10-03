using System.Net;
using Orkeon.Application.Interfaces.AgentCommunication;
using Orkeon.Infrastructure.AgentCommunication;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Network;

namespace Orkeon.Infrastructure.Tests.A2A;

/// <summary>
/// GAP-10: since GAP-09 an Orkeon server validates the credential on its task endpoints, so the
/// client sends one — <c>A2A:Security:ClientAuthScheme</c> + the secret named by
/// <c>ClientCredentialSecretName</c>, read through the <c>ISecretProvider</c> on every call.
/// </summary>
public sealed class A2AClientAuthenticationTests
{
    private const string Remote = "http://remote:5002";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (A2AClient Client, FakeHttpMessageHandler Handler) Client(
        A2ASecurityOptions? security, MockSecretProvider? secrets)
    {
        var handler = new FakeHttpMessageHandler();
        handler.SetResponse($"{Remote}/a2a/tasks/send", HttpStatusCode.OK, """{"taskId":"t1","status":"Completed"}""");
        handler.SetResponse($"{Remote}/a2a/tasks/t1", HttpStatusCode.OK, """{"taskId":"t1","status":"Completed"}""");
        var client = new A2AClient(
            new FakeHttpClientFactory(handler), new A2AOptions(), new FakeFileSystemService(), security,
            secretProvider: secrets);
        return (client, handler);
    }

    private static MockSecretProvider Secrets(string name, string value)
    {
        var secrets = new MockSecretProvider();
        secrets.AddSecret(name, value);
        return secrets;
    }

    [Theory]
    [InlineData("ApiKey")]
    [InlineData("Bearer")]
    public async Task EveryTaskCall_CarriesTheConfiguredCredential(string scheme)
    {
        var (client, handler) = Client(
            new A2ASecurityOptions { ClientAuthScheme = scheme, ClientCredentialSecretName = "PEER_CREDENTIAL" },
            Secrets("PEER_CREDENTIAL", "k-123456"));
        using (client)
        {
            await client.SendTaskAsync(new Uri(Remote), new A2ATaskRequest { Id = "t1", SkillId = "s", Input = "x" }, Ct);
            await client.GetTaskStatusAsync(new Uri(Remote), "t1", Ct);
            await client.CancelTaskAsync(new Uri(Remote), "t1", Ct);
        }

        Assert.Equal(3, handler.AuthorizationHeaders.Count);
        Assert.All(handler.AuthorizationHeaders, h => Assert.Equal($"{scheme} k-123456", h));
    }

    [Fact]
    public async Task NoScheme_SendsNoAuthorizationHeader()
    {
        var (client, handler) = Client(new A2ASecurityOptions(), secrets: null);
        using (client)
            await client.SendTaskAsync(new Uri(Remote), new A2ATaskRequest { Id = "t1", SkillId = "s", Input = "x" }, Ct);

        Assert.Null(Assert.Single(handler.AuthorizationHeaders));
    }

    [Fact]
    public async Task ASchemeWhoseCredentialCannotBeRead_FailsBeforeAnyRequest()
    {
        var security = new A2ASecurityOptions { ClientAuthScheme = "ApiKey", ClientCredentialSecretName = "PEER_CREDENTIAL" };

        var (withoutProvider, handler1) = Client(security, secrets: null);
        using (withoutProvider)
            await Assert.ThrowsAsync<InvalidOperationException>(() => withoutProvider.SendTaskAsync(
                new Uri(Remote), new A2ATaskRequest { Id = "t1", SkillId = "s", Input = "x" }, Ct));

        var (withoutSecret, handler2) = Client(security, new MockSecretProvider());
        using (withoutSecret)
            await Assert.ThrowsAsync<InvalidOperationException>(() => withoutSecret.SendTaskAsync(
                new Uri(Remote), new A2ATaskRequest { Id = "t1", SkillId = "s", Input = "x" }, Ct));

        var (unknownScheme, handler3) = Client(
            new A2ASecurityOptions { ClientAuthScheme = "Basic", ClientCredentialSecretName = "PEER_CREDENTIAL" },
            Secrets("PEER_CREDENTIAL", "k"));
        using (unknownScheme)
            await Assert.ThrowsAsync<InvalidOperationException>(() => unknownScheme.SendTaskAsync(
                new Uri(Remote), new A2ATaskRequest { Id = "t1", SkillId = "s", Input = "x" }, Ct));

        Assert.Empty(handler1.AuthorizationHeaders);
        Assert.Empty(handler2.AuthorizationHeaders);
        Assert.Empty(handler3.AuthorizationHeaders);
    }

    [Fact]
    public async Task AnOrkeonClient_IsAcceptedByAnOrkeonServerThatRequiresAnApiKey()
    {
        var secrets = Secrets("A2A_PEER_KEY", "k-123456");
        var (server, port) = await A2ALoopback.StartAsync(options => new A2AServer(
            options, new StubA2ATaskRouter(),
            security: new A2ASecurityOptions { AllowedAuthSchemes = { "ApiKey" }, ApiKeySecretNames = { "A2A_PEER_KEY" } },
            secretProvider: secrets), Ct);
        try
        {
            var httpFactory = new RealHttpClientFactory();
            using var client = new A2AClient(
                httpFactory, new A2AOptions(), new FakeFileSystemService(),
                new A2ASecurityOptions { ClientAuthScheme = "ApiKey", ClientCredentialSecretName = "A2A_PEER_KEY" },
                secretProvider: secrets);
            using var anonymous = new A2AClient(httpFactory, new A2AOptions(), new FakeFileSystemService());

            var accepted = await client.SendTaskAsync(
                new Uri($"{LoopbackPorts.Host}:{port}"), new A2ATaskRequest { Id = "auth-1", SkillId = "s", Input = "x" }, Ct);
            Assert.Equal(A2ATaskStatus.Completed, accepted.Status);

            var refused = await Assert.ThrowsAsync<HttpRequestException>(() => anonymous.SendTaskAsync(
                new Uri($"{LoopbackPorts.Host}:{port}"), new A2ATaskRequest { Id = "auth-2", SkillId = "s", Input = "x" }, Ct));
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        }
        finally
        {
            await server.DisposeAsync();
        }
    }
}

/// <summary>A fresh real <see cref="HttpClient"/> per call — the A2A client disposes each one.</summary>
internal sealed class RealHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new();
}
