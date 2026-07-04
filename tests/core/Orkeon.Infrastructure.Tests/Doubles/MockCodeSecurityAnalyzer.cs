using Orkeon.Application.Interfaces;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock for ICodeSecurityAnalyzer with call tracking and configurable results.
/// </summary>
public class MockCodeSecurityAnalyzer : ICodeSecurityAnalyzer
{
    private CodeSecurityReport _analyzeResult = new()
    {
        IsAllowed = true,
        OverallRisk = SecurityRiskLevel.None,
        Violations = Array.Empty<SecurityViolation>()
    };

    private Func<string, SecurityAnalysisOptions?, CodeSecurityReport>? _analyzeFunc;

    // --- Tracking ---
    public int AnalyzeCallCount { get; private set; }
    public string? LastAnalyzedCode { get; private set; }
    public SecurityAnalysisOptions? LastAnalysisOptions { get; private set; }
    public List<string> AllAnalyzedCodes { get; } = [];

    // --- Configuration ---
    public void SetAnalyzeResult(CodeSecurityReport result) => _analyzeResult = result;

    public void SetAllowed() =>
        _analyzeResult = new CodeSecurityReport
        {
            IsAllowed = true,
            OverallRisk = SecurityRiskLevel.None,
            Violations = Array.Empty<SecurityViolation>()
        };

    public void SetAnalyzeFunc(Func<string, SecurityAnalysisOptions?, CodeSecurityReport> func) =>
        _analyzeFunc = func;

    public void SetBlocked(SecurityRiskLevel risk, params SecurityViolation[] violations) =>
        _analyzeResult = new CodeSecurityReport
        {
            IsAllowed = false,
            OverallRisk = risk,
            Violations = violations
        };

    // --- ICodeSecurityAnalyzer ---
    public CodeSecurityReport Analyze(string code, SecurityAnalysisOptions? options = null)
    {
        AnalyzeCallCount++;
        LastAnalyzedCode = code;
        LastAnalysisOptions = options;
        AllAnalyzedCodes.Add(code);
        return _analyzeFunc != null ? _analyzeFunc(code, options) : _analyzeResult;
    }
}
