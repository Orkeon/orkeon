using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.AgentCommunication;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.AgentCommunication;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.Network;

namespace Orkeon.Infrastructure.Tests.A2A;

public class A2ASecurityTests
{
    /// <summary>Generates a self-signed PFX, writes it under a temp dir, and returns (dir, fileName).</summary>
    private static (string root, string fileName) WriteSelfSignedPfx()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=orkeon-a2a-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var pfxBytes = cert.Export(X509ContentType.Pfx);

        var root = Path.Combine(Path.GetTempPath(), "orkeon-a2a-certs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var fileName = "client.pfx";
        File.WriteAllBytes(Path.Combine(root, fileName), pfxBytes);
        return (root, fileName);
    }

    [Fact]
    public void SecurityOptions_ShouldHaveSecureDefaults()
    {
        // Arrange & Act
        var options = new A2ASecurityOptions();

        // Assert
        Assert.Null(options.ClientCertificatePath);
        Assert.Null(options.ClientCertificatePassword);
        Assert.Empty(options.TrustedCertificateAuthorities);
        Assert.False(options.RequireMutualTls);
        Assert.Empty(options.AllowedAuthSchemes);
    }

    [Fact]
    public void SecurityOptions_ShouldAcceptMtlsConfiguration()
    {
        // Arrange & Act
        var options = new A2ASecurityOptions
        {
            ClientCertificatePath = "/certs/client.pfx",
            ClientCertificatePassword = "secret123",
            RequireMutualTls = true,
            TrustedCertificateAuthorities =
            {
                "/certs/ca1.pem",
                "/certs/ca2.pem"
            },
            AllowedAuthSchemes = { "Bearer", "mTLS" }
        };

        // Assert
        Assert.Equal("/certs/client.pfx", options.ClientCertificatePath);
        Assert.Equal("secret123", options.ClientCertificatePassword);
        Assert.True(options.RequireMutualTls);
        Assert.Equal(2, options.TrustedCertificateAuthorities.Count);
        Assert.Contains("/certs/ca1.pem", options.TrustedCertificateAuthorities);
        Assert.Equal(2, options.AllowedAuthSchemes.Count);
    }

    [Fact]
    public void A2AOptions_ShouldHaveCorrectDefaults()
    {
        // Arrange & Act
        var options = new A2AOptions();

        // Assert
        Assert.False(options.EnableServer);
        Assert.Equal(5002, options.Port);
        Assert.Equal("http://localhost", options.Host);
        Assert.Equal("Orkeon", options.AgentName);
        Assert.Equal("Orkeon A2A Agent", options.AgentDescription);
        Assert.Equal("1.0.0", options.AgentVersion);
        Assert.Null(options.Organization);
        Assert.Null(options.ContactUrl);
        Assert.Equal(30, options.TimeoutSeconds);
    }

    [Fact]
    public void A2AOptions_ShouldAcceptServerConfiguration()
    {
        // Arrange & Act
        var options = new A2AOptions
        {
            EnableServer = true,
            Port = 8080,
            Host = "https://myagent.example.com",
            AgentName = "MyAgent",
            AgentDescription = "My custom agent",
            AgentVersion = "2.0.0",
            Organization = "MyOrg",
            ContactUrl = new Uri("https://myorg.com"),
            TimeoutSeconds = 60
        };

        // Assert
        Assert.True(options.EnableServer);
        Assert.Equal(8080, options.Port);
        Assert.Equal("https://myagent.example.com", options.Host);
        Assert.Equal("MyAgent", options.AgentName);
        Assert.Equal(60, options.TimeoutSeconds);
    }

    // --- R3.4: mTLS client wiring ---------------------------------------------------------

    [Fact]
    public async Task SecurityHandlerFactory_ShouldPresentClientCertificate_FromConfiguredPath()
    {
        // Arrange — a real self-signed cert reachable through the VFS
        var (root, fileName) = WriteSelfSignedPfx();
        try
        {
            var fs = new MockFileSystemService(root);
            var security = new A2ASecurityOptions
            {
                ClientCertificatePath = "/" + fileName
            };

            // Act
            using var handler = await A2ASecurityHandlerFactory.CreateAsync(security, fs, ct: TestContext.Current.CancellationToken);

            // Assert — the client certificate from ClientCertificatePath is loaded and presented
            var certificates = handler.SslOptions.ClientCertificates;
            Assert.NotNull(certificates);
            var presented = Assert.IsType<X509Certificate2>(Assert.Single(certificates.Cast<X509Certificate>()));
            Assert.Equal("CN=orkeon-a2a-test", presented.Subject);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SecurityHandlerFactory_ShouldNotPresentCertificate_WhenNoPathConfigured()
    {
        // Arrange
        var fs = new MockFileSystemService(Path.GetTempPath());
        var security = new A2ASecurityOptions(); // no client cert

        // Act
        using var handler = await A2ASecurityHandlerFactory.CreateAsync(security, fs, ct: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(handler.SslOptions.ClientCertificates is null or { Count: 0 });
    }

    [Fact]
    public async Task SecurityHandlerFactory_ShouldThrow_WhenCertificateMissing()
    {
        // Arrange
        var fs = new MockFileSystemService(Path.GetTempPath());
        var security = new A2ASecurityOptions { ClientCertificatePath = "/does-not-exist.pfx" };

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => A2ASecurityHandlerFactory.CreateAsync(security, fs, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SecurityHandlerFactory_ShouldUseDefaultValidation_WhenNoTrustedCasConfigured()
    {
        // Arrange — default options: no client cert, no pinned CAs.
        var fs = new MockFileSystemService(Path.GetTempPath());
        var security = new A2ASecurityOptions();

        // Act
        using var handler = await A2ASecurityHandlerFactory.CreateAsync(security, fs, ct: TestContext.Current.CancellationToken);

        // Assert — no custom callback is installed, so the OS trust store validates the server
        // certificate. There is no accept-any-certificate opt-out (validation cannot be disabled).
        Assert.Null(handler.SslOptions.RemoteCertificateValidationCallback);
    }

    // --- R3.4: server auth scheme enforcement ---------------------------------------------

    // --- GAP-09: a declared scheme is a validated credential, never just a header shape ------

    private static A2ACredentialValidator Validator(
        A2ASecurityOptions security,
        IAuthenticationProvider[]? bearer = null,
        ISecretProvider? secrets = null)
        => new(security, bearer ?? [], secrets);

    [Fact]
    public async Task Validator_AcceptsAnyRequest_WhenNoSchemeIsDeclared()
    {
        var validator = Validator(new A2ASecurityOptions());

        var result = await validator.ValidateAsync(null, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer")]
    [InlineData("Bearer   ")]
    [InlineData("Basic dXNlcjpwYXNz")]
    public async Task Validator_RejectsAMissingMalformedOrUndeclaredCredential(string? header)
    {
        var validator = Validator(
            new A2ASecurityOptions { AllowedAuthSchemes = { "Bearer" } },
            bearer: [new StubAuthenticationProvider("good-token")]);

        var result = await validator.ValidateAsync(header, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Validator_RejectsABearerTokenNoProviderValidates()
    {
        var provider = new StubAuthenticationProvider("good-token");
        var validator = Validator(
            new A2ASecurityOptions { AllowedAuthSchemes = { "Bearer" } }, bearer: [provider]);

        var result = await validator.ValidateAsync("Bearer abc.def.ghi", TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Equal(["abc.def.ghi"], provider.SeenTokens);
    }

    [Fact]
    public async Task Validator_AcceptsABearerToken_WhenOneProviderValidatesIt()
    {
        var validator = Validator(
            new A2ASecurityOptions { AllowedAuthSchemes = { "Bearer" } },
            bearer: [new StubAuthenticationProvider("other"), new StubAuthenticationProvider("good-token")]);

        var result = await validator.ValidateAsync("bearer good-token", TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
        Assert.Equal("Bearer", result.Scheme);
        Assert.Equal("peer", result.Principal?.FindFirst("sub")?.Value);
    }

    [Fact]
    public async Task Validator_AcceptsOnlyAnApiKeyReadFromTheSecretProvider()
    {
        var secrets = new MockSecretProvider();
        secrets.AddSecret("A2A_PEER_KEY", "k-123456");
        var validator = Validator(
            new A2ASecurityOptions { AllowedAuthSchemes = { "ApiKey" }, ApiKeySecretNames = { "A2A_PEER_KEY" } },
            secrets: secrets);

        Assert.True((await validator.ValidateAsync("ApiKey k-123456", TestContext.Current.CancellationToken)).IsValid);
        Assert.False((await validator.ValidateAsync("ApiKey k-1234567", TestContext.Current.CancellationToken)).IsValid);
        Assert.False((await validator.ValidateAsync("ApiKey k-12345", TestContext.Current.CancellationToken)).IsValid);
        Assert.False((await validator.ValidateAsync("Bearer k-123456", TestContext.Current.CancellationToken)).IsValid);
    }

    [Fact]
    public async Task Validator_IsReady_OnlyWhenEveryDeclaredSchemeHasAValidator()
    {
        var ct = TestContext.Current.CancellationToken;
        var secrets = new MockSecretProvider();
        secrets.AddSecret("A2A_PEER_KEY", "k-123456");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Validator(
            new A2ASecurityOptions { AllowedAuthSchemes = { "Bearer" } }).EnsureReadyAsync(ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Validator(
            new A2ASecurityOptions { AllowedAuthSchemes = { "ApiKey" } }, secrets: secrets).EnsureReadyAsync(ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Validator(
            new A2ASecurityOptions { AllowedAuthSchemes = { "ApiKey" }, ApiKeySecretNames = { "MISSING" } }, secrets: secrets).EnsureReadyAsync(ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Validator(
            new A2ASecurityOptions { AllowedAuthSchemes = { "Basic" } }).EnsureReadyAsync(ct));

        await Validator(new A2ASecurityOptions { AllowedAuthSchemes = { "Bearer" } },
            bearer: [new StubAuthenticationProvider("t")]).EnsureReadyAsync(ct);
        await Validator(new A2ASecurityOptions { AllowedAuthSchemes = { "ApiKey" }, ApiKeySecretNames = { "A2A_PEER_KEY" } },
            secrets: secrets).EnsureReadyAsync(ct);
    }

    [Fact]
    public void AddOrkeonA2A_RegistersABearerValidator_PerFilledSection()
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["A2A:Security:AllowedAuthSchemes:0"] = "Bearer",
                ["A2A:Security:Oidc:Authority"] = "https://id.example.test/realms/orkeon",
                ["A2A:Security:Oidc:ClientId"] = "orkeon",
                ["A2A:Security:AzureAD:TenantId"] = "contoso",
                ["A2A:Security:AzureAD:ClientId"] = "a2a-api",
                ["A2A:Security:AzureAD:ValidAudiences:0"] = "api://a2a",
            })
            .Build();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();

        services.AddOrkeonA2A(configuration);

        var validators = services.Where(d => d.ServiceType == typeof(IAuthenticationProvider))
            .Select(d => d.ImplementationInstance).ToList();
        Assert.Contains(validators, v => v is Orkeon.Infrastructure.Security.Auth.OidcAuthProvider);
        Assert.Contains(validators, v => v is Orkeon.Infrastructure.Security.Auth.AzureAdAuthProvider);
    }

    [Fact]
    public void AddOrkeonA2A_RegistersNoBearerValidator_WithoutAFilledSection()
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["A2A:Security:Oidc:Authority"] = "https://id.example.test" })
            .Build();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();

        services.AddOrkeonA2A(configuration);

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IAuthenticationProvider));
    }

    [Fact]
    public async Task Server_StartAsync_ShouldThrow_WhenASchemeIsDeclaredWithoutAValidator()
    {
        // Refused before it binds anything: a probed port is enough.
        var options = A2ALoopback.Options(LoopbackPorts.Probe());
        var security = new A2ASecurityOptions { AllowedAuthSchemes = { "Bearer" } };
        await using var server = new A2AServer(options, new StubA2ATaskRouter(), logger: null, security: security);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => server.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains("Bearer", ex.Message, StringComparison.Ordinal);
        Assert.False(server.IsRunning);
    }

    [Theory]
    [InlineData("Bearer abc.def.ghi", HttpStatusCode.Unauthorized)]
    [InlineData("Bearer good-token", HttpStatusCode.OK)]
    [InlineData("ApiKey not-the-key", HttpStatusCode.Unauthorized)]
    [InlineData("ApiKey k-123456", HttpStatusCode.OK)]
    public async Task Server_SubmitsATask_OnlyWithAValidatedCredential(string authorization, HttpStatusCode expected)
    {
        var ct = TestContext.Current.CancellationToken;
        var router = new StubA2ATaskRouter();
        var secrets = new MockSecretProvider();
        secrets.AddSecret("A2A_PEER_KEY", "k-123456");
        var security = new A2ASecurityOptions
        {
            AllowedAuthSchemes = { "Bearer", "ApiKey" },
            ApiKeySecretNames = { "A2A_PEER_KEY" },
        };
        var (server, port) = await A2ALoopback.StartAsync(options => new A2AServer(
            options, router, logger: null, security: security,
            authenticationProviders: [new StubAuthenticationProvider("good-token")], secretProvider: secrets), ct);

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{LoopbackPorts.Host}:{port}/a2a/tasks/send")
            {
                Content = new StringContent(
                    System.Text.Json.JsonSerializer.Serialize(new A2ATaskRequest { Id = "t-1", SkillId = "researcher", Input = "hi" }),
                    System.Text.Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation("Authorization", authorization);

            using var response = await client.SendAsync(request, ct);

            Assert.Equal(expected, response.StatusCode);
            Assert.Equal(expected == HttpStatusCode.OK, router.LastRequest is not null);
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task Server_ShouldReject_WhenMutualTlsRequiredButNoClientCertificate()
    {
        // Arrange — RequireMutualTls but the client connects over plain HTTP (no cert).
        // R9.1: a trust anchor is now mandatory to start (fail-closed), hence the pin.
        var router = new StubA2ATaskRouter();
        var security = new A2ASecurityOptions
        {
            RequireMutualTls = true,
            TrustedClientCertificateThumbprints = { "0000000000000000000000000000000000000000" }
        };
        var (server, port) = await A2ALoopback.StartAsync(
            options => new A2AServer(options, router, logger: null, security: security), TestContext.Current.CancellationToken);

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            // Act
            var response = await client.GetAsync($"{LoopbackPorts.Host}:{port}/a2a/tasks/some-id", TestContext.Current.CancellationToken);

            // Assert — mTLS required, no client cert ⇒ 403 (and the task is NOT processed)
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task Server_ShouldReject_WhenAuthSchemeRequiredButHeaderMissing()
    {
        // Arrange
        var router = new StubA2ATaskRouter();
        var security = new A2ASecurityOptions { AllowedAuthSchemes = { "Bearer" } };
        var (server, port) = await A2ALoopback.StartAsync(options => new A2AServer(options, router, logger: null, security: security,
            authenticationProviders: [new StubAuthenticationProvider("good-token")]), TestContext.Current.CancellationToken);

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            // Act — no Authorization header
            var response = await client.GetAsync($"{LoopbackPorts.Host}:{port}/a2a/tasks/some-id", TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task Server_ShouldAllowDiscovery_EvenWhenAuthRequired()
    {
        // Arrange — discovery (agent card) must stay public even with auth enabled
        var router = new StubA2ATaskRouter();
        var security = new A2ASecurityOptions { AllowedAuthSchemes = { "Bearer" } };
        var (server, port) = await A2ALoopback.StartAsync(options => new A2AServer(options, router, logger: null, security: security,
            authenticationProviders: [new StubAuthenticationProvider("good-token")]), TestContext.Current.CancellationToken);

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            // Act
            var response = await client.GetAsync($"{LoopbackPorts.Host}:{port}/.well-known/agent.json", TestContext.Current.CancellationToken);

            // Assert — discovery is reachable without credentials
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    // --- R3.4: GET /a2a/tasks/{id} no longer fabricates 200/Pending -----------------------

    [Fact]
    public async Task GetTask_ShouldReturn501_InsteadOfFabricated200Pending()
    {
        // Arrange — no security, no task persistence
        var router = new StubA2ATaskRouter();
        var (server, port) = await A2ALoopback.StartAsync(options => new A2AServer(options, router), TestContext.Current.CancellationToken);

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            // Act
            var response = await client.GetAsync($"{LoopbackPorts.Host}:{port}/a2a/tasks/unknown-task-id", TestContext.Current.CancellationToken);

            // Assert — explicit 501, never a fabricated 200/Pending
            Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.DoesNotContain("pending", body, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    // --- R9.1 (SEC-012): server-side client-certificate authentication ---------------------

    /// <summary>Creates a self-signed CA and a leaf certificate signed by it.</summary>
    private static (X509Certificate2 Ca, X509Certificate2 Leaf) CreateCaAndSignedLeaf()
    {
        using var caKey = RSA.Create(2048);
        var caRequest = new CertificateRequest(
            "CN=orkeon-a2a-test-ca", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(
            certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));
        var ca = caRequest.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(30));

        using var leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest(
            "CN=orkeon-a2a-test-client", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var serial = new byte[8];
        RandomNumberGenerator.Fill(serial);
        var leaf = leafRequest.Create(
            ca, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(10), serial);

        return (ca, leaf);
    }

    /// <summary>Creates a standalone self-signed certificate with the given validity window.</summary>
    private static X509Certificate2 CreateSelfSignedCert(DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=orkeon-a2a-test-selfsigned", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(notBefore, notAfter);
    }

    /// <summary>Builds an unstarted server whose mTLS trust is initialized from the given options.</summary>
    private static A2AServer CreateServerWithTrust(A2ASecurityOptions security)
    {
        var server = new A2AServer(
            new A2AOptions(), new StubA2ATaskRouter(), logger: null, security: security);
        server.InitializeMutualTlsTrust();
        return server;
    }

    [Fact]
    public async Task Server_StartAsync_ShouldThrow_WhenMutualTlsRequiredWithoutTrustAnchor()
    {
        // Arrange — RequireMutualTls with neither CAs nor pinned thumbprints: starting must
        // fail closed instead of silently accepting any date-valid certificate. Refused before it
        // binds anything: a probed port is enough.
        var options = A2ALoopback.Options(LoopbackPorts.Probe());
        var security = new A2ASecurityOptions { RequireMutualTls = true };
        await using var server = new A2AServer(
            options, new StubA2ATaskRouter(), logger: null, security: security);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => server.StartAsync(TestContext.Current.CancellationToken));
        Assert.Contains("RequireMutualTls", ex.Message, StringComparison.Ordinal);
        Assert.False(server.IsRunning);
    }

    [Fact]
    public void IsClientCertificateTrusted_ShouldRejectSelfSignedWithValidDates_WhenCaTrustConfigured()
    {
        // SEC-012 regression: on e91ef936 any date-valid certificate passed the mTLS guard.
        // A self-signed certificate (valid dates, not pinned, not chained to the CA) must be rejected.
        var (ca, leaf) = CreateCaAndSignedLeaf();
        using var _ = ca;
        using var __ = leaf;
        using var selfSigned = CreateSelfSignedCert(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(10));

        var root = Path.Combine(Path.GetTempPath(), "orkeon-a2a-ca-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var caPath = Path.Combine(root, "ca.cer");
            File.WriteAllBytes(caPath, ca.Export(X509ContentType.Cert));

            using var server = CreateServerWithTrust(new A2ASecurityOptions
            {
                RequireMutualTls = true,
                TrustedCertificateAuthorities = { caPath }
            });

            Assert.False(server.IsClientCertificateTrusted(selfSigned));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IsClientCertificateTrusted_ShouldAcceptCertificate_ChainedToConfiguredCa()
    {
        // Functional non-regression: a client certificate issued by the configured CA is accepted.
        var (ca, leaf) = CreateCaAndSignedLeaf();
        using var _ = ca;
        using var __ = leaf;

        var root = Path.Combine(Path.GetTempPath(), "orkeon-a2a-ca-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var caPath = Path.Combine(root, "ca.cer");
            File.WriteAllBytes(caPath, ca.Export(X509ContentType.Cert));

            using var server = CreateServerWithTrust(new A2ASecurityOptions
            {
                RequireMutualTls = true,
                TrustedCertificateAuthorities = { caPath }
            });

            Assert.True(server.IsClientCertificateTrusted(leaf));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IsClientCertificateTrusted_ShouldAcceptPinnedThumbprint_WithinValidityWindow()
    {
        using var pinned = CreateSelfSignedCert(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(10));

        using var server = CreateServerWithTrust(new A2ASecurityOptions
        {
            RequireMutualTls = true,
            TrustedClientCertificateThumbprints = { pinned.Thumbprint }
        });

        Assert.True(server.IsClientCertificateTrusted(pinned));
    }

    [Fact]
    public void IsClientCertificateTrusted_ShouldRejectPinnedThumbprint_WhenExpired()
    {
        using var expired = CreateSelfSignedCert(
            DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddDays(-1));

        using var server = CreateServerWithTrust(new A2ASecurityOptions
        {
            RequireMutualTls = true,
            TrustedClientCertificateThumbprints = { expired.Thumbprint }
        });

        Assert.False(server.IsClientCertificateTrusted(expired));
    }

    // --- R9.1 (SEC-011): client-side pinning never bypasses host-name validation -----------

    [Fact]
    public void ValidateAgainstTrustedCas_ShouldRejectNameMismatch_EvenWhenChainedToPinnedCa()
    {
        // SEC-011 regression: on e91ef936 a certificate chained to a pinned CA was accepted
        // regardless of the TLS errors — including a host-name mismatch (identity bypass).
        var (ca, leaf) = CreateCaAndSignedLeaf();
        using var _ = ca;
        using var __ = leaf;
        var pins = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ca.Thumbprint };

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.CustomTrustStore.Add(ca);
        Assert.True(chain.Build(leaf)); // sanity: the leaf really chains to the pinned CA

        var errors = SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch;
        Assert.False(A2ASecurityHandlerFactory.ValidateAgainstTrustedCas(leaf, chain, errors, pins));
    }

    [Fact]
    public void ValidateAgainstTrustedCas_ShouldAcceptChainErrorsOnly_WhenChainedToPinnedCa()
    {
        // Private-CA pinning keeps working: an unknown chain (CA absent from the OS store)
        // vouched for by the pin list is the one accepted degradation.
        var (ca, leaf) = CreateCaAndSignedLeaf();
        using var _ = ca;
        using var __ = leaf;
        var pins = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ca.Thumbprint };

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.CustomTrustStore.Add(ca);
        Assert.True(chain.Build(leaf));

        Assert.True(A2ASecurityHandlerFactory.ValidateAgainstTrustedCas(
            leaf, chain, SslPolicyErrors.RemoteCertificateChainErrors, pins));
    }

    [Fact]
    public void ValidateAgainstTrustedCas_ShouldRejectMissingCertificate()
    {
        var pins = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AA" };

        Assert.False(A2ASecurityHandlerFactory.ValidateAgainstTrustedCas(
            cert: null, chain: null, SslPolicyErrors.RemoteCertificateNotAvailable, pins));
    }

    // --- R13.2: CA-pinning is logged (the insecure opt-out was removed) ---------------------

    [Fact]
    public async Task SecurityHandlerFactory_ShouldLogPinning_WhenTrustedCasConfigured()
    {
        // Arrange — a configured trusted CA on disk and a recording logger.
        var (ca, leaf) = CreateCaAndSignedLeaf();
        using var _ = ca;
        using var __ = leaf;

        var root = Path.Combine(Path.GetTempPath(), "orkeon-a2a-pin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var caPath = Path.Combine(root, "ca.cer");
            await File.WriteAllBytesAsync(caPath, ca.Export(X509ContentType.Cert), TestContext.Current.CancellationToken);
            var fs = new MockFileSystemService(root);
            var security = new A2ASecurityOptions { TrustedCertificateAuthorities = { caPath } };
            var logger = new RecordingLogger();

            // Act
            using var handler = await A2ASecurityHandlerFactory.CreateAsync(
                security, fs, logger, TestContext.Current.CancellationToken);

            // Assert — pinning installs a validation callback and logs it at Information.
            Assert.NotNull(handler.SslOptions.RemoteCertificateValidationCallback);
            Assert.True(logger.HasEntry(e =>
                e.Level == LogLevel.Information
                && e.Message.Contains("pinned", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // --- R9.2 (ANT-018): cached mTLS handler — one PFX read, pooled connections ------------

    [Fact]
    public async Task A2AClient_ShouldReadClientCertificateOnce_AcrossMultipleCalls()
    {
        // Arrange — a real A2A server (no security) and a client configured for mTLS whose
        // VFS is instrumented. On e91ef936 every A2A call re-read and re-imported the PFX
        // (new handler + new client + full handshake per call).
        var (root, fileName) = WriteSelfSignedPfx();
        A2AServer? server = null;
        try
        {
            (server, var port) = await A2ALoopback.StartAsync(
                options => new A2AServer(options, new StubA2ATaskRouter()), TestContext.Current.CancellationToken);

            var countingFs = new CountingFileSystemService(new MockFileSystemService(root));
            var security = new A2ASecurityOptions { ClientCertificatePath = "/" + fileName };
            using var fakeHandler = new FakeHttpMessageHandler();
            using var client = new A2AClient(
                new FakeHttpClientFactory(fakeHandler),
                new A2AOptions { TimeoutSeconds = 5 },
                countingFs,
                security);

            // Act — two calls over the mTLS-configured path (plain HTTP endpoint: the client
            // certificate is only consumed on TLS handshakes, which is irrelevant here — the
            // point is the handler construction count).
            var first = await client.SendTaskAsync(
                new Uri($"{LoopbackPorts.Host}:{port}"), new A2ATaskRequest { Id = "t1", SkillId = "s", Input = "i" },
                TestContext.Current.CancellationToken);
            var second = await client.SendTaskAsync(
                new Uri($"{LoopbackPorts.Host}:{port}"), new A2ATaskRequest { Id = "t2", SkillId = "s", Input = "i" },
                TestContext.Current.CancellationToken);

            // Assert — both calls succeeded and the PFX was read exactly once
            Assert.Equal("t1", first.TaskId);
            Assert.Equal("t2", second.TaskId);
            Assert.Equal(1, countingFs.ReadAllBytesCalls);
        }
        finally
        {
            if (server is not null)
                await server.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A2AClient_Dispose_ShouldBeIdempotent_AndBlockFurtherMtlsCalls()
    {
        // Arrange — mTLS-configured client; the cached handler (and imported certificate)
        // is released on Dispose, after which secure calls are refused.
        var (root, fileName) = WriteSelfSignedPfx();
        try
        {
            var security = new A2ASecurityOptions { ClientCertificatePath = "/" + fileName };
            using var fakeHandler = new FakeHttpMessageHandler();
            var client = new A2AClient(
                new FakeHttpClientFactory(fakeHandler),
                new A2AOptions { TimeoutSeconds = 5 },
                new MockFileSystemService(root),
                security);

            // Act — double dispose is safe
            client.Dispose();
            client.Dispose();

            // Assert — the mTLS path is closed after disposal (thrown before any network I/O)
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => client.SendTaskAsync(
                    new Uri("http://localhost:1"), new A2ATaskRequest { Id = "t", SkillId = "s", Input = "i" },
                    TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
