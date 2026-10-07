using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Teams;

/// <summary>
/// « Rephrase » on step 1 (STUDIO-57): the assistant's LLM rewrites the need as a clear brief —
/// what comes in, what comes out, the rules stated — through <c>orkeon forge rephrase</c>, one
/// child process that opens no session. The rewritten text replaces the need; what the user had
/// typed stays one click away (« Back to my words ») until the creation is started over.
/// </summary>
public sealed partial class CreateTeamViewModel
{
    private string? _needBeforeRephrase;
    private bool _isRephrasing;

    /// <summary>« Rephrase »: asks the assistant for a clearer need.</summary>
    public AsyncRelayCommand RephraseNeedCommand { get; private set; } = null!;

    /// <summary>« Back to my words »: the need as the user typed it, before the first rephrase.</summary>
    public RelayCommand UndoRephraseCommand { get; private set; } = null!;

    /// <summary>Whether a rephrase is under way — the chip says so, and nothing else runs.</summary>
    public bool IsRephrasing => _isRephrasing;

    /// <summary>Whether the assistant can be asked: a profile, a need, no engine and no rephrase running.</summary>
    public bool CanRephrase => HasAssistant && !IsEngineRunning && !_isRephrasing && _need.Trim().Length > 0;

    /// <summary>Whether the user's own words can be brought back.</summary>
    public bool CanUndoRephrase => _needBeforeRephrase is not null && !_isRephrasing;

    private void InitializeRephrase()
    {
        RephraseNeedCommand = new AsyncRelayCommand(RephraseNeedAsync, () => CanRephrase);
        UndoRephraseCommand = new RelayCommand(UndoRephrase, () => CanUndoRephrase);
    }

    [SuppressMessage("Design", "CA1031",
        Justification = "The call's own fault barrier: a launcher that throws must land on the status line, not in a MessageBox.")]
    private async Task RephraseNeedAsync()
    {
        if (!CanRephrase)
            return;

        var original = Need;
        _isRephrasing = true;
        StatusMessage = _strings[StudioStringKeys.WizardRephrasing];
        RefreshRephrase();

        ForgeRephraseResult result;
        try
        {
            result = await _client.RephraseAsync(
                new ForgeRephraseRequest
                {
                    Need = original,
                    WorkingDirectory = _workspace,
                    EnvironmentOverrides = AssistantEnvironment(),
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            result = new ForgeRephraseResult(null, $"{exception.GetType().Name}: {exception.Message}");
        }

        _dispatcher.Post(() =>
        {
            _isRephrasing = false;
            if (result.Success)
            {
                // The first rephrase keeps the user's words; a second one keeps them still.
                _needBeforeRephrase ??= original;
                Need = result.Text!;
                StatusMessage = _strings[StudioStringKeys.WizardRephrased];
            }
            else
            {
                StatusMessage = _strings.Format(StudioStringKeys.WizardRephraseFailed, result.Error);
            }

            RefreshRephrase();
        });
    }

    private void UndoRephrase()
    {
        if (_needBeforeRephrase is not { } words)
            return;

        _needBeforeRephrase = null;
        Need = words;
        StatusMessage = "";
        RefreshRephrase();
    }

    /// <summary>Forgets the words kept for « Back to my words » — another creation starts.</summary>
    private void ClearRephrase()
    {
        _needBeforeRephrase = null;
        _isRephrasing = false;
        RefreshRephrase();
    }

    private void RefreshRephrase()
    {
        OnPropertiesChanged(nameof(IsRephrasing), nameof(CanRephrase), nameof(CanUndoRephrase));
        RephraseNeedCommand?.RaiseCanExecuteChanged();
        UndoRephraseCommand?.RaiseCanExecuteChanged();
    }
}
