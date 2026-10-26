using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Orkeon.Studio.Wpf.Controls;

/// <summary>The state mark of a <see cref="StepTab"/> (refonte R14): one meaning per mark.</summary>
public enum StepTabState
{
    /// <summary>An empty circle: optional, or nothing in it yet.</summary>
    Empty,

    /// <summary>A green check: the section is done.</summary>
    Done,

    /// <summary>An amber dot: an action of the user's is expected there — the step's wait point (R13).</summary>
    Expected,

    /// <summary>An amber dot and the subtitle in the warning tone: something there needs fixing.</summary>
    Warning,

    /// <summary>A green dot: the section was checked and holds.</summary>
    Verified,

    /// <summary>A turning ring: the engine is working on the section.</summary>
    Working,
}

/// <summary>
/// One tab of a <see cref="StepTabs"/> classeur (R14): its state mark, its title (the Header)
/// and a 12 px state subtitle under it. Its look is the implicit style in Themes/Components.xaml.
/// The grouped variant (Settings, refonte lot 11): a tab whose <see cref="Group"/> is set opens a
/// group, and the tabs after it, with no group, belong to it. The classeur then draws the groups
/// as a horizontal bar above the column, which lists only the active group's tabs.
/// </summary>
public sealed class StepTab : TabItem
{
    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(StepTab), new FrameworkPropertyMetadata(""));

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(StepTabState), typeof(StepTab), new FrameworkPropertyMetadata(StepTabState.Empty, OnGroupingChanged));

    public static readonly DependencyProperty GroupProperty = DependencyProperty.Register(
        nameof(Group), typeof(string), typeof(StepTab), new FrameworkPropertyMetadata("", OnGroupingChanged));

    private static readonly DependencyPropertyKey IsOutsideActiveGroupPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsOutsideActiveGroup), typeof(bool), typeof(StepTab), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsOutsideActiveGroupProperty = IsOutsideActiveGroupPropertyKey.DependencyProperty;

    static StepTab()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(StepTab), new FrameworkPropertyMetadata(typeof(StepTab)));
        // The group bar counts the visible tabs: it follows the tab's visibility (the expert-only
        // tabs), as it follows its state and its group through their own registrations.
        VisibilityProperty.OverrideMetadata(typeof(StepTab), new FrameworkPropertyMetadata(Visibility.Visible, OnGroupingChanged));
    }

    public string Subtitle { get => (string)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public StepTabState State { get => (StepTabState)GetValue(StateProperty); set => SetValue(StateProperty, value); }

    /// <summary>The title of the group this tab opens; empty for a tab inside a group, or with none.</summary>
    public string Group { get => (string)GetValue(GroupProperty); set => SetValue(GroupProperty, value); }

    /// <summary>True when the classeur is grouped and this tab belongs to another group than the active one: it draws nothing.</summary>
    public bool IsOutsideActiveGroup
    {
        get => (bool)GetValue(IsOutsideActiveGroupProperty);
        internal set => SetValue(IsOutsideActiveGroupPropertyKey, value);
    }

    private static void OnGroupingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        (ItemsControl.ItemsControlFromItemContainer(d) as StepTabs)?.RefreshGroups();
}

/// <summary>One entry of the group bar of a grouped <see cref="StepTabs"/>: its title, how many sections it shows, its dot.</summary>
public sealed class StepTabGroup : INotifyPropertyChanged
{
    private string _label = "";
    private int _count;
    private bool _hasDot;
    private bool _isActive;

    internal StepTabGroup(int index) => Index = index;

    /// <summary>The group's place in the bar.</summary>
    public int Index { get; }

    /// <summary>The group's title, in capitals.</summary>
    public string Label { get => _label; internal set => Set(ref _label, value); }

    /// <summary>How many of its sections show (the expert-only ones are not counted in Novice).</summary>
    public int Count { get => _count; internal set => Set(ref _count, value); }

    /// <summary>True when one of its sections waits for the user or needs fixing (Expected, Warning).</summary>
    public bool HasDot { get => _hasDot; internal set => Set(ref _hasDot, value); }

    /// <summary>True for the group whose sections the column lists.</summary>
    public bool IsActive { get => _isActive; internal set => Set(ref _isActive, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>
/// The vertical classeur of a step with sections (R14): a 232 px column of <see cref="StepTab"/>
/// on the left, the selected section's pane on the right. The active tab runs into the pane;
/// the space under the tabs holds <see cref="SideContent"/> — the Novice help card, nothing in
/// Expert. The pane's own footer belongs to each section's content; the step's action bar
/// stays outside the classeur.
/// <para>
/// Grouped (refonte lot 11): when a tab carries a <see cref="StepTab.Group"/>, the groups become
/// a horizontal bar above the classeur (<see cref="Groups"/>) and the column lists the active
/// group's tabs only. The active group follows the selected tab, so a section shown from
/// elsewhere (the Diagnostic) brings its group; a group chosen in the bar opens the section last
/// visited there, else its first. Left and right move from group to group.
/// </para>
/// </summary>
public sealed class StepTabs : TabControl
{
    public static readonly DependencyProperty SideContentProperty = DependencyProperty.Register(
        nameof(SideContent), typeof(object), typeof(StepTabs), new FrameworkPropertyMetadata(null));

    private static readonly DependencyPropertyKey HasGroupsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasGroups), typeof(bool), typeof(StepTabs), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty HasGroupsProperty = HasGroupsPropertyKey.DependencyProperty;

    private readonly Dictionary<int, StepTab> _lastVisited = [];
    private ItemsControl? _groupBar;
    private int _activeGroup = -1;
    private bool _refreshing;

    static StepTabs()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(StepTabs), new FrameworkPropertyMetadata(typeof(StepTabs)));
    }

    /// <summary>Builds the classeur; the group bar's command picks a group.</summary>
    public StepTabs() => SelectGroupCommand = new GroupCommand(this);

    public object? SideContent { get => GetValue(SideContentProperty); set => SetValue(SideContentProperty, value); }

    /// <summary>The group bar's entries, in order; empty when no tab carries a group.</summary>
    public ObservableCollection<StepTabGroup> Groups { get; } = [];

    /// <summary>True when the tabs carry groups: the bar shows.</summary>
    public bool HasGroups
    {
        get => (bool)GetValue(HasGroupsProperty);
        private set => SetValue(HasGroupsPropertyKey, value);
    }

    /// <summary>Picks the group given as parameter (a <see cref="StepTabGroup"/>).</summary>
    public ICommand SelectGroupCommand { get; }

    protected override DependencyObject GetContainerForItemOverride() => new StepTab();

    protected override bool IsItemItsOwnContainerOverride(object item) => item is StepTab;

    public override void OnApplyTemplate()
    {
        if (_groupBar is not null)
            _groupBar.PreviewKeyDown -= OnGroupBarKeyDown;

        base.OnApplyTemplate();
        _groupBar = GetTemplateChild("PART_GroupBar") as ItemsControl;
        if (_groupBar is not null)
            _groupBar.PreviewKeyDown += OnGroupBarKeyDown;
        RefreshGroups();
    }

    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        _lastVisited.Clear();
        RefreshGroups();
    }

    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectionChanged(e);
        RefreshGroups();
    }

    /// <summary>The tabs with the group each belongs to: the group a tab opens runs until the next one.</summary>
    private List<(StepTab Tab, int Group)> Membership()
    {
        var tabs = new List<(StepTab, int)>();
        var group = -1;
        foreach (var item in Items)
        {
            if (item is not StepTab tab)
                continue;

            if (tab.Group is { Length: > 0 })
                group++;
            tabs.Add((tab, Math.Max(group, 0)));
        }

        return tabs;
    }

    /// <summary>Rebuilds the bar from the tabs: titles, counts, dots, the active group, and which tabs draw.</summary>
    internal void RefreshGroups()
    {
        if (_refreshing)
            return;

        _refreshing = true;
        try
        {
            var tabs = Membership();
            var titles = tabs.Where(entry => entry.Tab.Group is { Length: > 0 }).Select(entry => entry.Tab.Group).ToList();
            HasGroups = titles.Count > 0;
            if (!HasGroups)
            {
                Groups.Clear();
                foreach (var (tab, _) in tabs)
                    tab.IsOutsideActiveGroup = false;
                return;
            }

            if (SelectedItem is StepTab selected && tabs.FirstOrDefault(entry => entry.Tab == selected) is { Tab: not null } current)
            {
                _activeGroup = current.Group;
                _lastVisited[current.Group] = selected;
            }

            _activeGroup = Math.Clamp(_activeGroup, 0, titles.Count - 1);
            while (Groups.Count > titles.Count)
                Groups.RemoveAt(Groups.Count - 1);
            while (Groups.Count < titles.Count)
                Groups.Add(new StepTabGroup(Groups.Count));

            for (var index = 0; index < titles.Count; index++)
            {
                var visible = tabs.Where(entry => entry.Group == index && entry.Tab.Visibility == Visibility.Visible).ToList();
                var group = Groups[index];
                group.Label = titles[index].ToUpper(System.Globalization.CultureInfo.CurrentUICulture);
                group.Count = visible.Count;
                group.HasDot = visible.Any(entry => entry.Tab.State is StepTabState.Expected or StepTabState.Warning);
                group.IsActive = index == _activeGroup;
            }

            foreach (var (tab, group) in tabs)
                tab.IsOutsideActiveGroup = group != _activeGroup;
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>Opens a group: the section last visited there when it still shows, else its first showing one.</summary>
    private void SelectGroup(int index)
    {
        var tabs = Membership().Where(entry => entry.Group == index && entry.Tab.Visibility == Visibility.Visible).Select(entry => entry.Tab).ToList();
        if (tabs.Count == 0)
            return;

        SelectedItem = _lastVisited.TryGetValue(index, out var last) && tabs.Contains(last) ? last : tabs[0];
    }

    /// <summary>Left and right on the bar: the previous or next group that shows a section, and the focus with it.</summary>
    private void OnGroupBarKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right) || Groups.Count == 0)
            return;

        var step = e.Key == Key.Left ? -1 : 1;
        for (var index = _activeGroup + step; index >= 0 && index < Groups.Count; index += step)
        {
            if (Groups[index].Count == 0)
                continue;

            SelectGroup(index);
            if (_groupBar?.ItemContainerGenerator.ContainerFromIndex(index) is DependencyObject container
                && VisualTreeHelper.GetChildrenCount(container) > 0
                && VisualTreeHelper.GetChild(container, 0) is UIElement button)
            {
                button.Focus();
            }

            break;
        }

        e.Handled = true;
    }

    /// <summary>The bar's command: the parameter is the group to open.</summary>
    private sealed class GroupCommand(StepTabs owner) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => parameter is StepTabGroup;

        public void Execute(object? parameter)
        {
            if (parameter is StepTabGroup group)
                owner.SelectGroup(group.Index);
        }
    }
}
