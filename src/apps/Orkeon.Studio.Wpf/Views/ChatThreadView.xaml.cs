using System.Collections.Specialized;
using System.Windows.Controls;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Views;

/// <summary>
/// Code-behind of the conversation panel. Its only jobs are the two things a binding cannot
/// express: a new bubble scrolls the thread to the bottom, the way every chat does, and the
/// thread's ask for the focus (R13, a question waits on the user) puts the caret in the composer.
/// </summary>
public partial class ChatThreadView : UserControl
{
    private INotifyCollectionChanged? _watched;
    private ChatThreadViewModel? _thread;

    /// <summary>Builds the panel and follows whichever thread it is given.</summary>
    public ChatThreadView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Follow(DataContext as ChatThreadViewModel);
    }

    private void Follow(ChatThreadViewModel? thread)
    {
        if (_watched is not null)
            _watched.CollectionChanged -= OnTurnsChanged;
        if (_thread is not null)
            _thread.FocusInputRequested -= OnFocusInputRequested;

        _watched = thread?.Turns;
        _thread = thread;
        if (_thread is not null)
            _thread.FocusInputRequested += OnFocusInputRequested;

        if (_watched is not null)
            _watched.CollectionChanged += OnTurnsChanged;
    }

    private void OnTurnsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.BeginInvoke(Scroll.ScrollToBottom);

    // After the layout: the panel may have just opened, and a collapsed box takes no focus.
    private void OnFocusInputRequested(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            Composer.Focus();
            Composer.CaretIndex = Composer.Text.Length;
        });
}
