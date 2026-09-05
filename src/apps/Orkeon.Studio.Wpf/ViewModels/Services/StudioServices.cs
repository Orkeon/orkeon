using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.History;
using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Targets;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Services;

/// <summary>
/// The seams a Studio ViewModel is built over, carried as one value rather than as a dozen
/// constructor parameters.
/// <para>
/// Every entry is optional and null means "take the production default", which is what lets a
/// test name only the two or three doubles its case needs, and lets the screenshot campaign
/// swap a single loader without restating the rest.
/// </para>
/// </summary>
public sealed record StudioServices
{
    /// <summary>Reads and writes the user's <c>appsettings.json</c>.</summary>
    public IAppSettingsStore? SettingsStore { get; init; }

    /// <summary>Answers whether a physical folder exists, and what it holds.</summary>
    public IDirectoryProbe? Directories { get; init; }

    /// <summary>Recognizes what a launch target is.</summary>
    public ITargetProbe? TargetProbe { get; init; }

    /// <summary>The file and folder browse dialogs.</summary>
    public IPathPicker? Picker { get; init; }

    /// <summary>The runner every screen invokes the co-installed CLI through.</summary>
    public OrkeonProcessRunner? ProcessRunner { get; init; }

    /// <summary>The per-user launch history.</summary>
    public ILaunchHistoryStore? HistoryStore { get; init; }

    /// <summary>Marshals a continuation back onto the UI thread.</summary>
    public IUiDispatcher? Dispatcher { get; init; }

    /// <summary>The localization port every fabricated string is resolved through.</summary>
    public IStudioStrings? Strings { get; init; }

    /// <summary>The forge engine behind the create-a-team wizard.</summary>
    public ForgeClient? ForgeClient { get; init; }

    /// <summary>Where the model profiles are read from and written to.</summary>
    public IModelProfileStore? ProfileStore { get; init; }

    /// <summary>Opens a folder or a link in the operator's shell.</summary>
    public IShellOpener? ShellOpener { get; init; }

    /// <summary>The "later" the assistant's timed beats ask for.</summary>
    public IUiDelay? Delay { get; init; }

    /// <summary>Probes an LLM endpoint for the "Test connection" command.</summary>
    public ILlmEndpointProbe? LlmProbe { get; init; }

    /// <summary>Peeks at the API keys the profiles name, without ever reading a file.</summary>
    public IApiKeyStore? KeyStore { get; init; }
}
