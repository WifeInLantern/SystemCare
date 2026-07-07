using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SystemCare.Controls;

/// <summary>A navigable tool in the command palette.</summary>
public sealed record NavEntry(string Title, string Category, Type PageType);

/// <summary>
/// A Ctrl+K command palette overlay: fuzzy-search all tools by name/category and Enter to navigate. Additive
/// and self-contained — it renders on top of the shell, closes on Esc / scrim click, and honours Reduce motion.
/// </summary>
public partial class CommandPalette : UserControl
{
    private IReadOnlyList<NavEntry> _all = [];

    /// <summary>Raised with the page type to navigate to when the user picks an entry.</summary>
    public event Action<Type>? Invoked;

    public bool IsOpen => Visibility == Visibility.Visible;

    public CommandPalette() => InitializeComponent();

    /// <summary>Supplies the full tool list (built from the live nav so nothing is duplicated).</summary>
    public void SetCatalog(IReadOnlyList<NavEntry> entries) => _all = entries;

    public void Toggle()
    {
        if (IsOpen) Close(); else Open();
    }

    public void Open()
    {
        Visibility = Visibility.Visible;
        SearchBox.Text = "";
        ApplyFilter("");
        if (Helpers.Animations.ReduceMotion)
            Opacity = 1;
        else
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
        // Focus the box after it becomes visible.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => SearchBox.Focus()));
    }

    public void Close()
    {
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        Visibility = Visibility.Collapsed;
    }

    private void OnScrimClick(object sender, MouseButtonEventArgs e) => Close();

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => ApplyFilter(SearchBox.Text);

    private void ApplyFilter(string query)
    {
        query = query.Trim();
        IEnumerable<NavEntry> results;
        if (query.Length == 0)
        {
            results = _all.OrderBy(e => e.Category).ThenBy(e => e.Title);
        }
        else
        {
            string q = query.ToLowerInvariant();
            results = _all
                .Where(e => e.Title.ToLowerInvariant().Contains(q) || e.Category.ToLowerInvariant().Contains(q))
                .OrderByDescending(e => e.Title.ToLowerInvariant().StartsWith(q))
                .ThenBy(e => e.Title);
        }

        ResultsList.ItemsSource = results.ToList();
        if (ResultsList.Items.Count > 0) ResultsList.SelectedIndex = 0;
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
            case Key.Down:
                Move(+1);
                e.Handled = true;
                break;
            case Key.Up:
                Move(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                InvokeSelected();
                e.Handled = true;
                break;
        }
    }

    private void Move(int delta)
    {
        int count = ResultsList.Items.Count;
        if (count == 0) return;
        int index = Math.Clamp(ResultsList.SelectedIndex + delta, 0, count - 1);
        ResultsList.SelectedIndex = index;
        ResultsList.ScrollIntoView(ResultsList.SelectedItem);
    }

    private void OnResultsClick(object sender, MouseButtonEventArgs e)
    {
        // Only invoke when the click landed on an actual row, not empty list space.
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is { DataContext: NavEntry entry })
            Invoke(entry);
    }

    private void InvokeSelected()
    {
        if (ResultsList.SelectedItem is NavEntry entry) Invoke(entry);
    }

    private void Invoke(NavEntry entry)
    {
        Close();
        Invoked?.Invoke(entry.PageType);
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T match) return match;
            node = VisualTreeHelper.GetParent(node);
        }
        return null;
    }
}
