using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Domain.Tools.Security;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Security;

namespace Orkeon.Infrastructure.Tests.Security;

public class UrlValidatorTestsFixture
{
    private readonly ILogger<UrlValidator> _logger = NullLogger<UrlValidator>.Instance;
    private UrlSecurityOptions _options = new()
    {
        ResolveDNS = false
    };

    // --- Fluent configuration ---

    public UrlValidatorTestsFixture WithAllowedDomain(string domain)
    {
        _options.AllowedDomains.Add(domain);
        return this;
    }

    public UrlValidatorTestsFixture WithBlockedDomain(string domain)
    {
        _options.BlockedDomains.Add(domain);
        return this;
    }

    public UrlValidatorTestsFixture WithPrivateIPBlocking(bool enabled)
    {
        _options.BlockPrivateIPs = enabled;
        return this;
    }

    public UrlValidatorTestsFixture WithOptions(Action<UrlSecurityOptions> configure)
    {
        configure(_options);
        return this;
    }

    // --- Build / Execution ---

    public UrlValidator Build()
        => new(Options.Create(_options), _logger);

    public async Task<UrlValidationResult> ValidateUrlAsync(string? url)
    {
        Uri? uri = string.IsNullOrWhiteSpace(url) ? null : new Uri(url, UriKind.RelativeOrAbsolute);
        return await Build().ValidateUrlAsync(uri!);
    }

    // --- Inspection ---

    public UrlSecurityOptions GetOptions() => _options;
}
