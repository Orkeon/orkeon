using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Security;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock for IPromptSanitizer with call tracking and configurable results.
/// </summary>
public class MockPromptSanitizer : IPromptSanitizer
{
    private SanitizationResult? _sanitizeResult;

    // --- Tracking ---
    public int SanitizeCallCount { get; private set; }
    public string? LastSanitizeInput { get; private set; }
    public SanitizationContext? LastSanitizeContext { get; private set; }

    public int WrapUserDataCallCount { get; private set; }
    public string? LastWrappedData { get; private set; }
    public string? LastSectionName { get; private set; }

    // --- Configuration ---
    public void SetSanitizeResult(SanitizationResult result) => _sanitizeResult = result;

    /// <summary>
    /// Configures sanitizer to pass through input unchanged (clean).
    /// </summary>
    public void SetPassThrough() => _sanitizeResult = null;

    /// <summary>
    /// Configures sanitizer to block all input.
    /// </summary>
    public void SetBlocked() => _sanitizeResult = SanitizationResult.Blocked(Array.Empty<ThreatDetection>());

    // --- IPromptSanitizer ---
    public SanitizationResult Sanitize(string input, SanitizationContext context)
    {
        SanitizeCallCount++;
        LastSanitizeInput = input;
        LastSanitizeContext = context;
        return _sanitizeResult ?? SanitizationResult.Clean(input);
    }

    public string WrapUserData(string data, string sectionName)
    {
        WrapUserDataCallCount++;
        LastWrappedData = data;
        LastSectionName = sectionName;
        return $"[{sectionName}]{data}[/{sectionName}]";
    }
}
