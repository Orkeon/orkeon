namespace Orkeon.Constants.Configuration;

/// <summary>
/// The sections Orkeon reads under its own names (GAP-40): every child of <c>Orkeon:</c> a component
/// reads — a C# opt-in's included —, and the children of the containers that only group sections.
/// <para>
/// A section name written wrong (<c>Orkeon:Guardain</c>) used to read as absent: its keys were
/// ignored, and the setting the operator meant ran on its default. Every shipped host now refuses a
/// child of <see cref="Containers"/> that is not in <see cref="Known"/>, naming the closest one. The
/// configuration root stays open: it also receives the environment variables without a prefix, and
/// nothing tells a misspelt section there from a variable of the machine.
/// </para>
/// <para>
/// A section a component starts to read is added here, or every host refuses it — and filed under a
/// category in <see cref="SettingsCategories"/>, which lists the sections of the root too.
/// </para>
/// </summary>
public static class SettingsSections
{
    /// <summary>
    /// The sections whose children are section names, not settings: <c>Orkeon</c>, its four groups
    /// (<c>Orkeon:Cli</c>, <c>Orkeon:Tools</c>, <c>Orkeon:Scripting</c>, <c>Orkeon:Security</c>) and the
    /// root <c>Security</c> group.
    /// </summary>
    public static IReadOnlyList<string> Containers { get; } =
    [
        "Orkeon",
        "Orkeon:Cli",
        "Orkeon:Tools",
        "Orkeon:Scripting",
        "Orkeon:Security",
        "Security",
    ];

    /// <summary>Every section a container may hold, by its full path.</summary>
    public static IReadOnlyList<string> Known { get; } =
    [
        "Orkeon:Checkpointing",
        "Orkeon:ChromaDb",
        "Orkeon:Cli",
        "Orkeon:Cli:ConsoleStreaming",
        "Orkeon:Cli:ScriptCommands",
        "Orkeon:Cli:ScriptHost",
        "Orkeon:Cli:Session",
        "Orkeon:Cli:Tui",
        "Orkeon:CodeSandbox",
        "Orkeon:CognitiveMemory",
        "Orkeon:Consensus",
        "Orkeon:CostTracking",
        "Orkeon:CrewFactory",
        "Orkeon:CrewMemory",
        "Orkeon:Dlp",
        "Orkeon:EmbeddingCache",
        "Orkeon:Embeddings",
        "Orkeon:Encryption",
        "Orkeon:ExecutionState",
        "Orkeon:FileSystem",
        "Orkeon:Guardian",
        "Orkeon:Host",
        "Orkeon:LanceDb",
        "Orkeon:Monitoring",
        "Orkeon:MultiModal",
        "Orkeon:Pinecone",
        "Orkeon:Rag",
        "Orkeon:Redis",
        "Orkeon:Sandbox",
        "Orkeon:Scripting",
        "Orkeon:Scripting:Limits",
        "Orkeon:Scripting:Toolchain",
        "Orkeon:Security",
        "Orkeon:Security:PermissionGate",
        "Orkeon:Sqlite",
        "Orkeon:TokenCounter",
        "Orkeon:Tools",
        "Orkeon:Tools:Email",
        "Orkeon:Tools:Shell",
        "Orkeon:VectorSearch",
        "Security:Audit",
        "Security:Prompt",
        "Security:ToolResults",
        "Security:Url",
        "Security:Vault",
    ];
}
