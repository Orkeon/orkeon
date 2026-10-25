using System.Collections.Specialized;
using System.Windows.Controls;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Views;

/// <summary>
/// Code-behind of the conversation panel. Its only jobs are the two things a binding cannot
/// express: a new bubble scrolls the thread to the bottom, the way every chat does, and the
/// thread's ask for the focus (R13, a question waits on the user) puts the caret in the composer,
/// or on the recommended answer of the question on screen (R15).
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
        {
            _thread.FocusInputRequested -= OnFocusInputRequested;
            _thread.FocusRepliesRequested -= OnFocusRepliesRequested;
        }

        _watched = thread?.Turns;
        _thread = thread;
        if (_thread is not null)
        {
            _thread.FocusInputRequested += OnFocusInputRequested;
            _thread.FocusRepliesRequested += OnFocusRepliesRequested;
        }

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

    // The recommended answer is the first button: it takes the focus, Enter presses it.
    private void OnFocusRepliesRequested(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            if (Replies.Items.Count > 0 && Replies.ItemContainerGenerator.ContainerFromIndex(0) is System.Windows.FrameworkElement first)
            {
                first.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First));
                first.BringIntoView();
            }
        });
}
