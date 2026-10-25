using System.Windows;
using System.Windows.Controls;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Views;

/// <summary>The create-a-team wizard. XAML wiring only; every behaviour lives in the ViewModel.</summary>
public partial class CreateTeamView : UserControl
{
    private CreateTeamViewModel? _wizard;

    /// <summary>Loads the XAML and follows the wizard's wait point (refonte R13).</summary>
    public CreateTeamView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_wizard is not null)
            _wizard.WaitPointReached -= OnWaitPointReached;
        _wizard = e.NewValue as CreateTeamViewModel;
        if (_wizard is not null)
            _wizard.WaitPointReached += OnWaitPointReached;
    }

    /// <summary>
    /// R13: a wait point reached, or « Waiting for you: … » clicked — the keyboard focus goes to
    /// the expected card's main button (Enter presses it), or into « Your question » when the
    /// answer goes to the assistant. After the layout, once the step and the tab are on screen.
    /// </summary>
    private void OnWaitPointReached(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, FocusWaitPoint);

    private void FocusWaitPoint()
    {
        if (_wizard is null)
            return;

        switch (_wizard.WaitPoint)
        {
            case WizardWaitPoint.ConfirmFolders:
                Reveal(ConfirmFoldersButton);
                break;
            case WizardWaitPoint.AnswerAssistant:
            case WizardWaitPoint.Decide:
                _wizard.Chat.RequestInputFocus();
                break;
            case WizardWaitPoint.ChooseModel:
                RevealFirst(AssistantGate);
                break;
            case WizardWaitPoint.ChooseFolder:
                RevealFirst(MountFoldersBlock);
                break;
            case WizardWaitPoint.Adopt:
                Reveal(AdoptButton);
                break;
        }
    }

    private static void Reveal(UIElement target)
    {
        target.Focus();
        (target as FrameworkElement)?.BringIntoView();
    }

    /// <summary>The first control of a card takes the focus: the card has no single main button.</summary>
    private static void RevealFirst(FrameworkElement card)
    {
        card.BringIntoView();
        card.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First));
    }

    /// <summary>
    /// Files dragged over the need (STUDIO-47): a copy, and the field lights up. Anything else —
    /// text dragged within the field — is left to the TextBox.
    /// </summary>
    private void OnNeedDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        e.Effects = DragDropEffects.Copy;
        ShowNeedDropTarget(true);
        e.Handled = true;
    }

    private void OnNeedDragLeave(object sender, DragEventArgs e) => ShowNeedDropTarget(false);

    /// <summary>The drop itself: the paths and the caret go to the wizard, the caret comes back after them.</summary>
    private void OnNeedDrop(object sender, DragEventArgs e)
    {
        ShowNeedDropTarget(false);
        if (DataContext is not CreateTeamViewModel wizard
            || e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths)
        {
            return;
        }

        NeedBox.CaretIndex = wizard.DropPaths(paths, NeedBox.CaretIndex);
        NeedBox.Focus();
        e.Handled = true;
    }

    private void ShowNeedDropTarget(bool on)
    {
        if (on)
        {
            NeedBox.SetResourceReference(Control.BorderBrushProperty, "AccentBrush");
            NeedBox.SetResourceReference(Control.BackgroundProperty, "AccentWeakBrush");
        }
        else
        {
            NeedBox.ClearValue(Control.BorderBrushProperty);
            NeedBox.ClearValue(Control.BackgroundProperty);
        }
    }

    /// <summary>Folds/unfolds the technical journal (pure presentation state on the VM).</summary>
    private void OnCopyTechJournal(object sender, RoutedEventArgs e)
    {
        if (DataContext is CreateTeamViewModel { RawLog.CanCopy: true } wizard)
        {
            try
            {
                // SetDataObject(copy: false) skips the flush that makes SetText throw when another
                // process (RDP, clipboard managers, VM tools) is holding the Win32 clipboard open.
                Clipboard.SetDataObject(wizard.RawLog.BuildText(), copy: false);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // The clipboard stayed busy: nothing to surface, the button can be pressed again.
            }
        }
    }

    /// <summary>
    /// STUDIO-13: copies the failure card's report — command line, exit code, engine error,
    /// the whole stderr — then shows "Copied!" for ~1.6 s, the diagnostic screen's pattern.
    /// </summary>
    private void OnCopyFailureReport(object sender, RoutedEventArgs e)
    {
        if (DataContext is CreateTeamViewModel { CanCopyFailureReport: true } wizard)
        {
            try
            {
                Clipboard.SetDataObject(wizard.BuildFailureReport(), copy: false);

                wizard.FailureReportCopied = true;
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
                timer.Tick += (_, _) => { wizard.FailureReportCopied = false; timer.Stop(); };
                timer.Start();
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // The clipboard stayed busy: nothing to surface, the button can be pressed again.
            }
        }
    }

    /// <summary>Copies the rendered crew YAML of the generated-definition card.</summary>
    private void OnCopyDefinition(object sender, RoutedEventArgs e)
    {
        if (DataContext is CreateTeamViewModel { HasCrewDefinition: true } wizard)
        {
            try
            {
                Clipboard.SetDataObject(wizard.CrewDefinitionYaml, copy: false);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // The clipboard stayed busy: nothing to surface, the button can be pressed again.
            }
        }
    }

    private void OnToggleTech(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.Teams.CreateTeamViewModel vm)
            vm.IsTechOpen = !vm.IsTechOpen;
    }
}
