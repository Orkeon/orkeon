using Orkeon.Domain.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Security;

namespace Orkeon.Infrastructure.Tests.Security;

public class ToolResultSanitizerTestsFixture
{
    private readonly ToolResultSanitizer _toolSanitizer;
    private readonly ToolResultSecurityOptions _toolOptions;

    public ToolResultSanitizerTestsFixture()
    {
        var promptOptions = Options.Create(new PromptSecurityOptions
        {
            Policy = SanitizationPolicy.Strip,
            EnableExfiltrationDetection = true
        });
        var promptSanitizer = new PromptSanitizer(promptOptions, NullLogger<PromptSanitizer>.Instance);

        _toolOptions = new ToolResultSecurityOptions
        {
            MaxToolResultLength = 50_000,
            Policy = SanitizationPolicy.Strip,
        };
        var toolOptionsWrapper = Options.Create(_toolOptions);

        _toolSanitizer = new ToolResultSanitizer(
            promptSanitizer,
            toolOptionsWrapper,
            NullLogger<ToolResultSanitizer>.Instance);
    }

    // --- Fluent configuration ---

    public ToolResultSanitizerTestsFixture WithTrustedTool(string toolName)
    {
        _toolOptions.TrustedTools.Add(toolName);
        return this;
    }

    // --- Execution ---

    public ToolResultSanitization SanitizeToolResult(
        string toolName, string? rawResult, string agentRole)
        => _toolSanitizer.SanitizeToolResult(toolName, rawResult!, agentRole);

    // --- Inspection ---

    public ToolResultSanitizer GetSanitizer() => _toolSanitizer;
    public ToolResultSecurityOptions GetOptions() => _toolOptions;
}
