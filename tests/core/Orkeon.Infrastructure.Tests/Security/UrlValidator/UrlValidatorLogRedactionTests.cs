using Microsoft.Extensions.Options;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Security;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Infrastructure.Tests.Security;

/// <summary>
/// The validator refuses a URL and then writes a line about it. Those lines are the one
/// place where a credential the validator just rejected could still reach a log file, so
/// what they carry is a security property, not a formatting choice.
/// </summary>
public class UrlValidatorLogRedactionTests
{
    private const string Password = "s3cr3t-fixture-value";

    private static (UrlValidator Validator, MockLogger<UrlValidator> Log) Create()
    {
        var log = new MockLogger<UrlValidator>();
        var options = Options.Create(new UrlSecurityOptions { ResolveDNS = false });
        return (new UrlValidator(options, log), log);
    }

    [Fact]
    public async Task ShouldNotLogTheCredentials_WhenRefusingAUrlThatEmbedsThem()
    {
        var (validator, log) = Create();

        var result = await validator.ValidateUrlAsync(
            new Uri($"https://admin:{Password}@example.com/private"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsAllowed);
        var logged = string.Join("\n", log.LogEntries.Select(e => e.Message));
        Assert.DoesNotContain(Password, logged, StringComparison.Ordinal);
        Assert.DoesNotContain("admin:", logged, StringComparison.Ordinal);
        // The line must stay actionable: the host it refused is still named.
        Assert.Contains("example.com", logged, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldNotLogTheQueryString_WhenRefusingAUrlOnScheme()
    {
        var (validator, log) = Create();

        var result = await validator.ValidateUrlAsync(
            new Uri($"ftp://example.com/x?token={Password}"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsAllowed);
        var logged = string.Join("\n", log.LogEntries.Select(e => e.Message));
        Assert.DoesNotContain(Password, logged, StringComparison.Ordinal);
        Assert.Contains("example.com", logged, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldNotLogTheQueryString_WhenRefusingABlockedPort()
    {
        var (validator, log) = Create();

        var result = await validator.ValidateUrlAsync(
            new Uri($"http://example.com:22/x?token={Password}"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsAllowed);
        var logged = string.Join("\n", log.LogEntries.Select(e => e.Message));
        Assert.DoesNotContain(Password, logged, StringComparison.Ordinal);
    }
}
