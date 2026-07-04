using CommandLine;

namespace Orkeon.Examples.Interactive.ClaimVerification;

/// <summary>
/// CLI options for the claim-verification interactive runner.
/// </summary>
public sealed class Options
{
    [Option('c', "config", Required = true,
        HelpText = "Path to crews/claim-verifier/config.yaml.")]
    public string ConfigPath { get; set; } = "";

    [Option('s', "settings", Required = false,
        HelpText = "Path to appsettings.json (defaults to same dir as config).")]
    public string? SettingsPath { get; set; }

    [Option("claims-root", Required = true,
        HelpText = "Directory containing claim-NNN-*.md files (typically <experiment>/inputs/claims).")]
    public string ClaimsRoot { get; set; } = "";

    [Option("rounds-root", Required = false,
        HelpText = "Directory under which interactive runs persist (defaults to ../rounds relative to claims-root).")]
    public string? RoundsRoot { get; set; }

    [Option('v', "verbose", Required = false, Default = 0,
        HelpText = "Verbosity for the per-verify host: 0=quiet, 1=LLM/tool exchanges, 2=full debug.")]
    public int Verbose { get; set; }

    [Option("llm-log", Required = false, Default = false,
        HelpText = "Enable LLM exchange logging on every verify run.")]
    public bool LlmLogEnabled { get; set; }

    [Option("ui", Required = false, Default = "auto",
        HelpText = "UI mode: tui (split-pane) | plain (legacy stdout/stdin) | auto. Default: auto.")]
    public string Ui { get; set; } = "auto";
}
