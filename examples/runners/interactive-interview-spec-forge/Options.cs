using CommandLine;

namespace Orkeon.Examples.Interactive.InterviewSpecForge;

/// <summary>
/// CLI options for the interview-spec-forge interactive runner.
/// </summary>
public sealed class Options
{
    [Option('c', "config", Required = true,
        HelpText = "Path to crews/interview-spec-forge/config.interactive.yaml.")]
    public string ConfigPath { get; set; } = "";

    [Option('s', "settings", Required = false,
        HelpText = "Path to appsettings.json (defaults to same dir as config).")]
    public string? SettingsPath { get; set; }

    [Option("transcripts-root", Required = true,
        HelpText = "Directory containing YYYY-MM-DD_personne_sujet.txt files (typically <experiment>/transcriptions).")]
    public string TranscriptsRoot { get; set; } = "";

    [Option("experiment-root", Required = false,
        HelpText = "Root of the experiment (defaults to parent of transcripts-root). Hosts rounds/, topics/, tasks/, glossary/.")]
    public string? ExperimentRoot { get; set; }

    [Option('v', "verbose", Required = false, Default = 0,
        HelpText = "Verbosity for the per-forge host: 0=quiet, 1=LLM/tool exchanges, 2=full debug.")]
    public int Verbose { get; set; }

    [Option("llm-log", Required = false, Default = true,
        HelpText = "Enable LLM exchange logging on every forge run (D23 acté : activé par défaut).")]
    public bool LlmLogEnabled { get; set; }

    [Option("ui", Required = false, Default = "auto",
        HelpText = "UI mode: tui (split-pane) | plain (legacy stdout/stdin) | auto. Default: auto.")]
    public string Ui { get; set; } = "auto";
}
