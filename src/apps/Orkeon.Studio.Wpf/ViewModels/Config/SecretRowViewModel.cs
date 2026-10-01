using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>
/// One API key row: the environment variable to fill, whether a value is in place, and the
/// paste-to-remember flow. The key value itself only ever travels to
/// <see cref="IApiKeyStore"/> — never into a file. Shared by the API-keys card of the model
/// tab (one row per variable the profiles name) and by the tool-keys card of the Tools tab
/// (one row per key a tool needs, STUDIO-21); the row knows nothing of either.
/// </summary>
public sealed class SecretRowViewModel : ObservableObject
{
    private readonly IApiKeyStore _keyStore;
    private readonly IStudioStrings _strings;
    private string _keyInput = "";
    private string? _storeError;

    /// <summary>Builds a row; <paramref name="consoleUrl"/> is where the key is issued, when the card can say so.</summary>
    public SecretRowViewModel(string envName, string usedBy, IApiKeyStore keyStore, IStudioStrings strings, Uri? consoleUrl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(envName);
        ArgumentNullException.ThrowIfNull(usedBy);
        ArgumentNullException.ThrowIfNull(keyStore);
        ArgumentNullException.ThrowIfNull(strings);

        EnvName = envName;
        UsedBy = usedBy;
        ConsoleUrl = consoleUrl?.ToString();
        _keyStore = keyStore;
        _strings = strings;
        StoreCommand = new AsyncRelayCommand(StoreAsync, () => _keyInput.Trim().Length > 0);
    }

    /// <summary>The environment variable holding the key (e.g. <c>DEEPSEEK_API_KEY</c>).</summary>
    public string EnvName { get; }

    /// <summary>What resolves this variable: the profile names, comma-joined, or the tool's name.</summary>
    public string UsedBy { get; }

    /// <summary>Where the key is issued, as text, when known; null hides the line.</summary>
    [SuppressMessage("Design", "CA1056",
        Justification = "Displayed as text next to the key row, never navigated by Studio.")]
    public string? ConsoleUrl { get; }

    /// <summary>Whether the row can say where to get a key.</summary>
    public bool HasConsoleUrl => ConsoleUrl is { Length: > 0 };

    /// <summary>Whether a value is currently in place.</summary>
    public bool HasKey => _keyStore.Peek(EnvName) is { Length: > 0 };

    /// <summary>"key remembered" / "no key detected", localized.</summary>
    public string StatusText => _strings[
        HasKey ? StudioStringKeys.ProfileKeyStatusSet : StudioStringKeys.ProfileKeyStatusMissing];

    /// <summary>The pasted key, cleared as soon as it is stored.</summary>
    public string KeyInput
    {
        get => _keyInput;
        set
        {
            if (SetProperty(ref _keyInput, value))
                StoreCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Why the last remembered key was not kept for the next sessions (STUDIO-44) — it is in
    /// place for this one all the same. Null when the last write went through.
    /// </summary>
    public string? StoreError
    {
        get => _storeError;
        private set
        {
            if (SetProperty(ref _storeError, value))
                OnPropertyChanged(nameof(HasStoreError));
        }
    }

    /// <summary>Whether <see cref="StoreError"/> has something to say.</summary>
    public bool HasStoreError => _storeError is not null;

    /// <summary>Stores the pasted key in the user environment and wipes the field.</summary>
    public AsyncRelayCommand StoreCommand { get; }

    /// <summary>
    /// Remembers the pasted key: in place for the session at once, kept for the next sessions
    /// off the interface thread, and a failure of that second half shown on the row.
    /// </summary>
    [SuppressMessage("Design", "CA1031",
        Justification = "The key is already in place for the session: whatever the user scope throws " +
                        "belongs on the row's line, not in the command's fault handler.")]
    public async Task StoreAsync()
    {
        var key = _keyInput.Trim();
        if (key.Length == 0)
            return;

        var persisted = _keyStore.SaveAsync(EnvName, key);
        KeyInput = "";
        StoreError = null;
        OnPropertiesChanged(nameof(HasKey), nameof(StatusText));

        try
        {
            await persisted.ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StoreError = string.Format(
                CultureInfo.CurrentCulture, _strings[StudioStringKeys.ProfileKeyPersistFailed], ex.Message);
        }

        OnPropertiesChanged(nameof(HasKey), nameof(StatusText));
    }
}
