using System.IO;
using Orkeon.Studio.Core.History;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Profiles;

namespace Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;

/// <summary>
/// A machine the campaign can photograph: a throwaway directory tree, the stores that read it,
/// and the CLI that answers for it.
/// <para>
/// Every store here is the REAL one, pointed at a seeded path. Screenshots taken over in-memory
/// doubles would prove the doubles render; screenshots over the real loaders prove the
/// application does.
/// </para>
/// </summary>
internal sealed class CaptureWorld
{
    /// <summary>The plan this world was written from.</summary>
    public required CaptureWorldPlan Plan { get; init; }

    /// <summary>Root of the world's own directory tree.</summary>
    public required string Root { get; init; }

    /// <summary>The <c>appsettings.json</c> the shell is pointed at.</summary>
    public required string SettingsPath { get; init; }

    /// <summary>Where adopted teams live.</summary>
    public required string TeamsRoot { get; init; }

    /// <summary>The workspace whose <c>.orkeon/forge</c> holds the sessions.</summary>
    public required string ForgeWorkspace { get; init; }

    /// <summary>Where the declared folders physically are.</summary>
    public required string DataDirectory { get; init; }

    /// <summary>The child process that never starts.</summary>
    public required ScriptedOrkeonCli Cli { get; init; }

    /// <summary>Binary resolution over this world's machine.</summary>
    public required OrkeonBinaryLocator Locator { get; init; }

    /// <summary>The real file-backed history store, over this world's file.</summary>
    public required ILaunchHistoryStore HistoryStore { get; init; }

    /// <summary>The real file-backed profile store, over this world's file.</summary>
    public required IModelProfileStore ProfileStore { get; init; }

    /// <summary>The endpoint probe that answers without a network.</summary>
    public OfflineLlmProbe LlmProbe { get; } = new();

    /// <summary>The key store that cannot reach the operator's environment.</summary>
    public EphemeralApiKeyStore KeyStore { get; } = new();

    /// <summary>The assistant's beats, holdable so a state between two of them can be photographed.</summary>
    public CaptureUiDelay Delay { get; } = new();

    /// <summary>A runner wired to this world's CLI and locator.</summary>
    public OrkeonProcessRunner Runner => new(Cli, Locator);

    /// <summary>The session directory of one seeded session.</summary>
    public string SessionDirectory(string slug) =>
        Path.Combine(ForgeWorkspace, ".orkeon", "forge", slug);

    /// <summary>The folder one seeded team was written to.</summary>
    public string TeamDirectory(string slug) => Path.Combine(TeamsRoot, slug);
}
