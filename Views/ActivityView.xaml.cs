using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinInstagram.Services;
using WinInstagram.ViewModels;

namespace WinInstagram.Views;

/// <summary>
/// Native, screen-reader friendly view of recent activity. Entries are real accessible list items
/// driven by Instagram's own activity payload, and opening one is raised to the shell so the shell
/// stays the single owner of engine navigation.
/// </summary>
public partial class ActivityView : UserControl
{
    public ActivityViewModel ViewModel => (ActivityViewModel)DataContext;

    public ActivityView()
    {
        InitializeComponent();
        DataContext = new ActivityViewModel();
    }

    /// <summary>Moves keyboard focus into the list so arrow keys read activity immediately.</summary>
    public void FocusItems()
    {
        if (ActivityList.Items.Count > 0)
        {
            ActivityList.SelectedIndex = Math.Max(0, ActivityList.SelectedIndex);
            ActivityList.ScrollIntoView(ActivityList.SelectedItem);
        }
        ActivityList.Focus();
        Keyboard.Focus(ActivityList);
    }

    private void UserControl_KeyDown(object sender, KeyEventArgs e)
    {
        // Refresh is read from the user's map so it can be rebound and preset-aware.
        if (ShortcutService.Instance.Matches(e.Key, Keyboard.Modifiers, "refresh"))
        {
            _ = ViewModel.RefreshAsync(announceNew: true);
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                ViewModel.OpenSelected();
                e.Handled = true;
                break;

            case Key.M:
                ViewModel.MarkAllSeen();
                e.Handled = true;
                break;
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = ViewModel.RefreshAsync(announceNew: true);
}
