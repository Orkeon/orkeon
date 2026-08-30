using System.Collections.Specialized;
using System.Windows.Controls;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Views;

/// <summary>
/// Code-behind of the conversation panel. Its only job is the one thing a binding cannot
/// express: a new bubble scrolls the thread to the bottom, the way every chat does.
/// </summary>
public partial class ChatThreadView : UserControl
{
    private INotifyCollectionChanged? _watched;

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

        _watched = thread?.Turns;

        if (_watched is not null)
            _watched.CollectionChanged += OnTurnsChanged;
    }

    private void OnTurnsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.BeginInvoke(Scroll.ScrollToBottom);
}
