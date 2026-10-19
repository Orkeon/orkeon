using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// The one check every application-level key handler runs before acting: while a modal dialog
/// (find, quit confirmation, a message box) holds the focus, the keyboard is the dialog's own.
/// </summary>
internal static class ModalFocus
{
    /// <summary>True when <paramref name="focused"/> (default: the focused view) sits inside a modal dialog.</summary>
    public static bool IsActive(View? focused = null)
    {
        for (var view = focused ?? Application.Navigation?.GetFocused(); view is not null; view = view.SuperView)
        {
            if (view is FindDialog or QuitConfirmDialog or Dialog) return true;
        }
        return false;
    }
}
