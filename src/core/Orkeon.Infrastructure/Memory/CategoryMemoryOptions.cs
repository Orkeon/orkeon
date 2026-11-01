namespace Orkeon.Infrastructure.Memory;

/// <summary>
/// The <c>Orkeon:Cli:Memory</c> section: whether the typed memory behind <c>memory_store</c> outlives the process,
/// and where it is kept.
/// </summary>
public sealed class CategoryMemoryOptions
{
    /// <summary>Configuration section.</summary>
    public const string SectionName = "Orkeon:Cli:Memory";

    /// <summary>
    /// Whether the typed memory (`memory_store`: user, project, feedback and reference entries) is kept per project under
    /// `RootDirectory` and read again at the next start; off unless a host turns it on, and the REPL turns it on when the
    /// key is absent. Off, the entries live in memory and die with the process.
    /// </summary>
    public bool Persist { get; set; }

    /// <summary>Virtual directory holding one `memory.json` per project, in the project's folder of the sessions.</summary>
    public string RootDirectory { get; set; } = "/workspace/.orkeon/memory";
}
