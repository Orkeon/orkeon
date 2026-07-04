using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Orkeon.Infrastructure.Configuration;
using UrlValidatorSut = Orkeon.Infrastructure.Security.UrlValidator;

namespace Orkeon.Infrastructure.Tests.Security;

public class UrlValidatorTests
{
    private readonly ILogger<UrlValidatorSut> _logger = NullLogger<UrlValidatorSut>.Instance;

    private UrlValidatorSut CreateValidator(Action<UrlSecurityOptions>? configure = null)
    {
        var options = new UrlSecurityOptions
        {
            // Disable DNS resolution by default to avoid network calls in tests
            ResolveDNS = false
        };
        configure?.Invoke(options);
        return new UrlValidatorSut(Options.Create(options), _logger);
    }

    [Fact]
    public async Task ShouldBeAllowed_WhenUrlIsPublic()
    {
        var validator = CreateValidator();
        var result = await validator.ValidateUrlAsync(new Uri("https://api.example.com/data"), TestContext.Current.CancellationToken);
        Assert.True(result.IsAllowed);
        Assert.NotNull(result.ValidatedUri);
        Assert.Equal("api.example.com", result.ValidatedUri!.Host);
    }

    [Fact]
    public async Task ShouldBeDenied_WhenUrlIsLoopback127()
    {
        var validator = CreateValidator();
        var result = await validator.ValidateUrlAsync(new Uri("http://127.0.0.1"), TestContext.Current.CancellationToken);
        Assert.False(result.IsAllowed);
        Assert.Contains("private", result.DenialReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldBeDenied_WhenUrlIsLocalhost()
    {
        var validator = CreateValidator();
        var result = await validator.ValidateUrlAsync(new Uri("http://localhost"), TestContext.Current.CancellationToken);
        Assert.False(result.IsAllowed);
        Assert.Contains("blocked", result.DenialReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldBeDenied_WhenUrlIsAwsMetadataEndpoint()
    {
        var validator = CreateValidator();
        var result = await validator.ValidateUrlAsync(new Uri("http://169.254.169.254/latest/meta-data/"), TestContext.Current.CancellationToken);
        Assert.False(result.IsAllowed);
        Assert.Contains("private", result.DenialReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldBeDenied_WhenUrlIsRfc1918_10Network()
    {
        var validator = CreateValidator();
        var result = await validator.ValidateUrlAsync(new Uri("http://10.0.0.1/admin"), TestContext.Current.CancellationToken);
        Assert.False(result.IsAllowed);
        Assert.Contains("private", result.DenialReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldBeDenied_WhenUrlIsRfc1918_172Network()
    {
        var validator = CreateValidator();
        var result = await validator.ValidateUrlAsync(new Uri("http://172.16.0.1/"), TestContext.Current.CancellationToken);
        Assert.False(result.IsAllowed);
        Assert.Contains("private", result.DenialReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldBeDenied_WhenUrlIsRfc1918_192Network()
    {
        var validator = CreateValidator();
        var result = await validator.ValidateUrlAsync(new Uri("http://192.168.1.1/"), TestContext.Current.CancellationToken);
        Assert.False(result.IsAllowed);
        Assert.Contains("private", result.DenialReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldBeDenied_WhenUrlIsIPv6Loopback()
    {
        var validator = CreateValidator();
        var result = await validator.ValidateUrlAsync(new Uri("http://[::1]/"), TestContext.Current.CancellationToken);
        Assert.False(result.IsAllowed);
        Assert.Contains("private", result.DenialReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldBeDenied_WhenUrlIsIPv6UniqueLocal()
    {
        var validator = CreateValidator();
        var result = await validator.ValidateUrlAsync(new Uri("http://[fc00::1]/"), TestContext.Current.CancellationToken);
        Assert.False(result.IsAllowed);
        Assert.Contains("private", result.DenialReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldBeDenied_WhenSchemeIsFtp()
    {
        var validator = CreateValidator();
        var result = await validator.ValidateUrlAsync(new Uri("ftp://example.com"), TestContext.Current.CancellationToken);
        Assert.False(result.IsAllowed);
        Assert.Contains("scheme", result.DenialReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldBeDenied_WhenUrlContainsCredentials()
    {
        var validator = CreateValidator();
        var result = await validator.ValidateUrlAsync(new Uri("http://admin:pass@example.com"), TestContext.Current.CancellationToken);
        Assert.False(result.IsAllowed);
        Assert.Contains("credentials", result.DenialReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldBeDenied_WhenPortIs6379()
    {
        var validator = CreateValidator();
        var result = await validator.ValidateUrlAsync(new Uri("http://example.com:6379/"), TestContext.Current.CancellationToken);
        Assert.False(result.IsAllowed);
        Assert.Contains("port", result.DenialReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldBeAllowed_WhenDomainIsInAllowlist()
    {
        var validator = CreateValidator(o => o.AllowedDomains.Add("example.com"));
        var result = await validator.ValidateUrlAsync(new Uri("https://example.com/api"), TestContext.Current.CancellationToken);
        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task ShouldBeAllowed_WhenSubdomainOfAllowedDomain()
    {
        var validator = CreateValidator(o => o.AllowedDomains.Add("example.com"));
        var result = await validator.ValidateUrlAsync(new Uri("https://api.example.com/data"), TestContext.Current.CancellationToken);
        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task ShouldBeDenied_WhenDomainNotInAllowlist()
    {
        var validator = CreateValidator(o => o.AllowedDomains.Add("example.com"));
        var result = await validator.ValidateUrlAsync(new Uri("https://evil.com/api"), TestContext.Current.CancellationToken);
        Assert.False(result.IsAllowed);
        Assert.Contains("allowed domains", result.DenialReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldBeDenied_WhenDomainIsInBlocklist()
    {
        var validator = CreateValidator(o => o.BlockedDomains.Add("evil.com"));
        var result = await validator.ValidateUrlAsync(new Uri("https://evil.com/api"), TestContext.Current.CancellationToken);
        Assert.False(result.IsAllowed);
        Assert.Contains("blocked", result.DenialReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldBeDenied_WhenSubdomainOfBlockedDomain()
    {
        var validator = CreateValidator(o => o.BlockedDomains.Add("evil.com"));
        var result = await validator.ValidateUrlAsync(new Uri("https://sub.evil.com/api"), TestContext.Current.CancellationToken);
        Assert.False(result.IsAllowed);
        Assert.Contains("blocked", result.DenialReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldBeDenied_WhenUrlIsInvalid()
    {
        var validator = CreateValidator();
        var result = await validator.ValidateUrlAsync(new Uri("not-a-url", UriKind.RelativeOrAbsolute), TestContext.Current.CancellationToken);
        Assert.False(result.IsAllowed);
        Assert.Contains("Invalid", result.DenialReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldBeDenied_WhenUrlIsZeroAddress()
    {
        var validator = CreateValidator();
        var result = await validator.ValidateUrlAsync(new Uri("http://0.0.0.0"), TestContext.Current.CancellationToken);
        Assert.False(result.IsAllowed);
        Assert.Contains("private", result.DenialReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldBeDenied_WhenUrlIsGoogleMetadata()
    {
        var validator = CreateValidator();
        var result = await validator.ValidateUrlAsync(new Uri("http://metadata.google.internal"), TestContext.Current.CancellationToken);
        Assert.False(result.IsAllowed);
        Assert.Contains("blocked", result.DenialReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ShouldBeDenied_WhenUrlIsEmptyOrNull(string? url)
    {
        var validator = CreateValidator();
        Uri? uri = string.IsNullOrWhiteSpace(url) ? null : new Uri(url, UriKind.RelativeOrAbsolute);
        var result = await validator.ValidateUrlAsync(uri!, TestContext.Current.CancellationToken);
        Assert.False(result.IsAllowed);
        Assert.Contains("null or empty", result.DenialReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldBeDenied_WhenUrlIsCgNatAddress()
    {
        var validator = CreateValidator();
        var result = await validator.ValidateUrlAsync(new Uri("http://100.64.0.1"), TestContext.Current.CancellationToken);
        Assert.False(result.IsAllowed);
        Assert.Contains("private", result.DenialReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldBeDenied_WhenPortIs22Ssh()
    {
        var validator = CreateValidator();
        var result = await validator.ValidateUrlAsync(new Uri("http://example.com:22/"), TestContext.Current.CancellationToken);
        Assert.False(result.IsAllowed);
        Assert.Contains("port", result.DenialReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldAllowPrivateIP_WhenPrivateIPBlockingDisabled()
    {
        var validator = CreateValidator(o => o.BlockPrivateIPs = false);
        var result = await validator.ValidateUrlAsync(new Uri("http://192.168.1.1/"), TestContext.Current.CancellationToken);
        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task ShouldBeAllowed_WhenPortIsStandardHttp()
    {
        var validator = CreateValidator();
        var result = await validator.ValidateUrlAsync(new Uri("https://example.com:443/api"), TestContext.Current.CancellationToken);
        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task ShouldBeDenied_WhenUrlIsInstanceDataHostname()
    {
        var validator = CreateValidator();
        var result = await validator.ValidateUrlAsync(new Uri("http://instance-data/latest/"), TestContext.Current.CancellationToken);
        Assert.False(result.IsAllowed);
        Assert.Contains("blocked", result.DenialReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldBeDenied_WhenUrlIsLoopback127_0_0_2()
    {
        var validator = CreateValidator();
        var result = await validator.ValidateUrlAsync(new Uri("http://127.0.0.2/"), TestContext.Current.CancellationToken);
        Assert.False(result.IsAllowed);
        Assert.Contains("private", result.DenialReason!, StringComparison.OrdinalIgnoreCase);
    }
}
