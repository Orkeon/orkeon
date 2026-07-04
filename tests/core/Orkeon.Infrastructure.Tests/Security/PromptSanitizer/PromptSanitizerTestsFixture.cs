using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Security;

namespace Orkeon.Infrastructure.Tests.Security;

public class PromptSanitizerTestsFixture
{
    private PromptSecurityOptions _options = new()
    {
        Policy = SanitizationPolicy.Strip,
        EnableExfiltrationDetection = true
    };
    private IPromptSanitizer? _sanitizer;

    // --- Fluent configuration ---

    public PromptSanitizerTestsFixture WithPolicy(SanitizationPolicy policy)
    {
        _options.Policy = policy;
        _sanitizer = null;
        return this;
    }

    public PromptSanitizerTestsFixture WithExfiltrationDetection(bool enabled)
    {
        _options.EnableExfiltrationDetection = enabled;
        _sanitizer = null;
        return this;
    }

    public PromptSanitizerTestsFixture WithCustomPatterns(params string[] patterns)
    {
        _options.CustomPatterns.Clear();
        foreach (var pattern in patterns)
            _options.CustomPatterns.Add(pattern);
        _sanitizer = null;
        return this;
    }

    public PromptSanitizerTestsFixture WithOptions(PromptSecurityOptions options)
    {
        _options = options;
        _sanitizer = null;
        return this;
    }

    // --- Build / Execution ---

    public IPromptSanitizer Build()
    {
        _sanitizer = new PromptSanitizer(
            Options.Create(_options),
            NullLogger<PromptSanitizer>.Instance);
        return _sanitizer;
    }

    public SanitizationResult Sanitize(string? input, SanitizationContext? context = null)
    {
        var sanitizer = _sanitizer ?? Build();
        return sanitizer.Sanitize(input!, context ?? DefaultContext);
    }

    public string WrapUserData(string data, string label)
    {
        var sanitizer = _sanitizer ?? Build();
        return sanitizer.WrapUserData(data, label);
    }

    // --- Inspection ---

    public IPromptSanitizer GetSanitizer() => _sanitizer ?? Build();

    // --- Defaults ---

    public static SanitizationContext DefaultContext =>
        new("test", "researcher", false);

    public static SanitizationContext TrustedContext =>
        new("system", "admin", true);
}
