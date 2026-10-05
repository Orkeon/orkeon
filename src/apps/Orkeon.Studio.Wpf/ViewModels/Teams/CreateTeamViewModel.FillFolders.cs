using System.Collections.ObjectModel;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Teams;

/// <summary>
/// One folder the session holds for the team (STUDIO-57): a confirmed folder kept inside the
/// team, the directory the engine created for it, and « Open », which shows that directory in
/// the file explorer so the user can drop files or sub-folders in it before the trial.
/// </summary>
public sealed class WizardHeldFolderRow
{
    private readonly IStudioStrings _strings;

    internal WizardHeldFolderRow(ForgeFolder folder, IStudioStrings strings, IShellOpener? opener)
    {
        _strings = strings;
        VirtualPath = folder.Path;
        IsInput = folder.IsInput;
        Directory = folder.Directory ?? "";
        CanOpen = opener is not null && Directory.Length > 0;
        OpenCommand = new RelayCommand(() => opener?.Open(Directory), () => CanOpen);
    }

    /// <summary>The name the agents use (<c>/inpdf</c>).</summary>
    public string VirtualPath { get; }

    /// <summary>Whether the team only reads it.</summary>
    public bool IsInput { get; }

    /// <summary>« read » or « written », in the user's language.</summary>
    public string RoleLabel => _strings[IsInput ? StudioStringKeys.WizardFolderRoleInput : StudioStringKeys.WizardFolderRoleOutput];

    /// <summary>The session directory behind it — where the files go.</summary>
    public string Directory { get; }

    /// <summary>Whether « Open » can show it: an opener is wired and the engine named the directory.</summary>
    public bool CanOpen { get; }

    /// <summary>« Open »: the directory in the file explorer.</summary>
    public RelayCommand OpenCommand { get; }
}

/// <summary>
/// The fill step (STUDIO-57): once the folders are confirmed, when one or more of them are kept
/// inside the team, the wizard asks whether the user wants to put files or sub-folders in them.
/// « No » lets the composition go on; « Yes » shows « Open » behind each such folder and
/// « Continue » below. The engine composes meanwhile — the plan does not depend on the files;
/// the trial, launched later by the user, reads them.
/// </summary>
public sealed partial class CreateTeamViewModel
{
    /// <summary>The held list the rows were last built from — a new one rebuilds them and asks again.</summary>
    private IReadOnlyList<ForgeFolder>? _heldShown;

    private bool _fillFoldersAnswered;
    private bool _isFillingFolders;

    /// <summary>The folders kept inside the team, in the confirmed order.</summary>
    public ObservableCollection<WizardHeldFolderRow> HeldFolderRows { get; } = [];

    /// <summary>Whether the fill card shows: folders are held, and the question is not settled.</summary>
    public bool IsFillFoldersStep => HeldFolderRows.Count > 0 && !_fillFoldersAnswered;

    /// <summary>Whether the card asks its question — « No » / « Yes ».</summary>
    public bool IsFillFoldersQuestion => IsFillFoldersStep && !_isFillingFolders;

    /// <summary>Whether the card shows the folders to fill, « Open » behind each, « Continue » below.</summary>
    public bool IsFillingFolders => IsFillFoldersStep && _isFillingFolders;

    /// <summary>« No »: nothing to drop, the construction goes on.</summary>
    public RelayCommand FillFoldersNoCommand { get; private set; } = null!;

    /// <summary>« Yes »: the folders to fill, each with « Open ».</summary>
    public RelayCommand FillFoldersYesCommand { get; private set; } = null!;

    /// <summary>« Continue »: the files are in place, the construction goes on.</summary>
    public RelayCommand FillFoldersContinueCommand { get; private set; } = null!;

    private void InitializeFillStep()
    {
        FillFoldersNoCommand = new RelayCommand(() => SettleFillStep(), () => IsFillFoldersQuestion);
        FillFoldersYesCommand = new RelayCommand(StartFillingFolders, () => IsFillFoldersQuestion);
        FillFoldersContinueCommand = new RelayCommand(() => SettleFillStep(), () => IsFillingFolders);
    }

    /// <summary>Rebuilds the rows when the engine announces the held folders (again), and republishes the card.</summary>
    private void SyncFillStep()
    {
        if (!ReferenceEquals(_model.HeldFolders, _heldShown))
        {
            _heldShown = _model.HeldFolders;
            HeldFolderRows.Clear();
            foreach (var held in _model.HeldFolders)
                HeldFolderRows.Add(new WizardHeldFolderRow(held, _strings, _shellOpener));

            _fillFoldersAnswered = false;
            _isFillingFolders = false;
        }

        RefreshFillStep();
    }

    private void StartFillingFolders()
    {
        _isFillingFolders = true;
        RefreshFillStep();
    }

    private void SettleFillStep()
    {
        _fillFoldersAnswered = true;
        _isFillingFolders = false;
        RefreshFillStep();
    }

    /// <summary>Forgets the card — another creation starts, or a session is resumed.</summary>
    private void ClearFillStep()
    {
        _heldShown = null;
        HeldFolderRows.Clear();
        _fillFoldersAnswered = false;
        _isFillingFolders = false;
        RefreshFillStep();
    }

    private void RefreshFillStep()
    {
        OnPropertiesChanged(nameof(IsFillFoldersStep), nameof(IsFillFoldersQuestion), nameof(IsFillingFolders));
        FillFoldersNoCommand.RaiseCanExecuteChanged();
        FillFoldersYesCommand.RaiseCanExecuteChanged();
        FillFoldersContinueCommand.RaiseCanExecuteChanged();
    }
}
