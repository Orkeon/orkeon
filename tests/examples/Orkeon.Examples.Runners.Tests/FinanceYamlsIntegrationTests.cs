using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.Common;
using Orkeon.Domain.Tools;
using Orkeon.Trading.Tools.Infrastructure.DependencyInjection;

namespace Orkeon.Examples.Runners.Tests;

/// <summary>
/// Verifies that every trading-tool name referenced by the 15 finance YAMLs
/// (after RUN-07 wiring) resolves through the same registry the runner uses.
/// Catches drift between YAML configs and tool registration.
/// </summary>
public partial class FinanceYamlsIntegrationTests
{
    /// <summary>The 44 tool names registered by AddTradingTools().</summary>
    private static readonly HashSet<string> KnownTradingTools = new(StringComparer.Ordinal);

    private static readonly string FinanceRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..",
            "examples", "03-finance-trading"));

    static FinanceYamlsIntegrationTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTradingTools();
        var sp = services.BuildServiceProvider();
        // Trading tools are registered under IBaseTool — the service type consumed by
        // ServiceProviderToolRegistry (MS DI does not upcast ITool registrations).
        foreach (var t in sp.GetServices<IBaseTool>())
            KnownTradingTools.Add(t.Name);
    }

    private static readonly string[] CaseDirNames =
    {
        "31-algo-trading",
        "32-fraud-detection",
        "33-credit-scoring",
        "34-portfolio-consensus",
        "35-accounting-reconciliation",
        "36-cash-flow-forecast",
        "37-kyc-aml-compliance",
        "38-contract-analysis",
        "39-robo-advisor",
        "40-invoice-processing",
        "41-insider-trading-detection",
        "42-dynamic-pricing",
        "43-tax-optimization",
        "44-esg-scoring",
        "45-stress-testing",
    };

    public static TheoryData<string> CaseDirs()
    {
        var data = new TheoryData<string>();
        foreach (var name in CaseDirNames)
            data.Add(name);
        return data;
    }

    [Theory]
    [MemberData(nameof(CaseDirs))]
    public void Yaml_references_at_least_two_real_trading_tools(string caseDir)
    {
        var path = Path.Combine(FinanceRoot, caseDir, "config.yaml");
        Assert.True(File.Exists(path), $"config not found: {path}");

        var yaml = File.ReadAllText(path);
        var refs = ExtractToolReferences(yaml).ToList();
        var trading = refs.Where(KnownTradingTools.Contains).Distinct().ToList();

        // Cases 38, 40, 43, 44 are weak fits (text/legal/static) — we accept 1+ for them.
        var weakFits = new HashSet<string> {
            "38-contract-analysis", "40-invoice-processing",
            "43-tax-optimization", "44-esg-scoring" };
        var minimum = weakFits.Contains(caseDir) ? 1 : 2;

        Assert.True(trading.Count >= minimum,
            $"{caseDir} references only {trading.Count} real trading tools " +
            $"(min={minimum}): [{string.Join(", ", trading)}]");
    }

    [Fact]
    public void All_yaml_trading_tool_references_resolve_via_AddTradingTools()
    {
        var unresolved = new List<string>();
        foreach (var caseDir in CaseDirNames)
        {
            var yaml = File.ReadAllText(Path.Combine(FinanceRoot, caseDir, "config.yaml"));
            foreach (var name in ExtractToolReferences(yaml).Distinct())
            {
                // We only care about names that LOOK trading-shaped (snake_case with
                // a finance-y prefix). Standard tools (http_api, json_tool, …) and
                // case-specific placeholder verbs (analyze_*, collect_*) are out of
                // scope for this assertion — they will be covered by the standard
                // registry separately.
                if (LooksLikeTradingToolName(name) && !KnownTradingTools.Contains(name))
                    unresolved.Add($"{caseDir}: {name}");
            }
        }
        Assert.True(unresolved.Count == 0,
            "Trading-shaped tool names that are NOT registered:\n" + string.Join("\n", unresolved));
    }

    private static IEnumerable<string> ExtractToolReferences(string yaml)
    {
        foreach (Match m in ToolReferenceRegex().Matches(yaml))
            yield return m.Groups[1].Value;
    }

    [GeneratedRegex(@"^\s+-\s+""([a-z_][a-z0-9_]*)""", RegexOptions.Multiline)]
    private static partial Regex ToolReferenceRegex();

    /// <summary>
    /// Trading-shaped = snake_case suffix matches a known finance domain
    /// (analysis, prediction, optimization, calculation, etc.). Used to scope
    /// which YAML refs we expect to resolve via AddTradingTools.
    /// </summary>
    private static bool LooksLikeTradingToolName(string name)
    {
        // Suffixes specific to ported trading tools (avoid generic "_data" since
        // YAML placeholder verbs like collect_data also match it).
        string[] suffixes =
        {
            "_analysis", "_prediction", "_optimization", "_calculation",
            "_classification", "_recognition", "_indicators",
            "_execution", "_routing", "_shortfall", "_management",
            "_trail", "_breaker", "_metrics", "_monitoring",
            "_parity", "_litterman", "_attribution", "_reporting",
            "_rebalancing", "_testing", "_exposure", "_depth",
        };
        // The bare names "alternative_data" / "fundamental_data" / etc. are listed
        // explicitly so we still catch them without matching every "*_data" placeholder.
        string[] explicitNames =
        {
            "alternative_data", "fundamental_data", "historical_data_fetch",
            "realtime_tick_data", "unified_market_data", "compliance_check",
        };
        return suffixes.Any(s => name.EndsWith(s, StringComparison.Ordinal))
            || explicitNames.Contains(name);
    }
}
