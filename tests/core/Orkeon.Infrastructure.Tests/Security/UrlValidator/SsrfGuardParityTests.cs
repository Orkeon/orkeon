using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Tests.Doubles;
using UrlValidatorSut = Orkeon.Infrastructure.Security.UrlValidator;

namespace Orkeon.Infrastructure.Tests.Security;

/// <summary>
/// The full <c>UrlValidator</c> (Infrastructure) and the fail-closed <c>DefaultSsrfGuard</c>
/// (Tools.Abstractions, used when no validator is injected) carry two copies of the same
/// private-range tables because the abstractions package cannot reference Infrastructure.
/// These tests are the guard against the copies drifting apart.
/// </summary>
public class SsrfGuardParityTests
{
    private static UrlValidatorSut CreateValidator()
    {
        // DNS resolution stays off: every case below is an IP literal, and a lookup would
        // make the test depend on the network.
        var options = new UrlSecurityOptions { ResolveDNS = false };
        return new UrlValidatorSut(Options.Create(options), NullLogger<UrlValidatorSut>.Instance);
    }

    [Theory]
    [InlineData("http://8.8.8.8/", true)]
    [InlineData("http://[2001:4860:4860::8888]/", true)]
    [InlineData("http://[::]/", false)]
    [InlineData("http://[::1]/", false)]
    [InlineData("http://[64:ff9b::7f00:1]/", false)]
    [InlineData("http://[::7f00:1]/", false)]
    [InlineData("http://[ff02::1]/", false)]
    [InlineData("http://169.254.169.254/", false)]
    public async Task BothGuardsShouldReachTheSameVerdict_WhenHostIsAnIpLiteral(string url, bool expectedAllowed)
    {
        var uri = new Uri(url);
        using var defaultGuardTool = new StubDefaultGuardHttpTool();

        var fromValidator = await CreateValidator().ValidateUrlAsync(uri, TestContext.Current.CancellationToken);
        var fromDefaultGuard = await defaultGuardTool.GuardAsync(uri, TestContext.Current.CancellationToken);

        Assert.Equal(expectedAllowed, fromValidator.IsAllowed);
        Assert.Equal(expectedAllowed, fromDefaultGuard.IsAllowed);
        Assert.Equal(fromValidator.IsAllowed, fromDefaultGuard.IsAllowed);
    }
}
