using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinInstagram.Services;
using WinInstagram.ViewModels;

namespace WinInstagram.Views;

/// <summary>
/// Native, screen-reader friendly stories navigator. The stories tray is a real accessible list and
/// the story on screen can be paused, so a story never advances out from under the user. Every
/// action is driven through the view model, which reports what actually happened.
/// </summary>
public partial class StoriesView : UserControl
{
    public StoriesViewModel ViewModel => (StoriesViewModel)DataContext;

    public StoriesView()
    {
        InitializeComponent();
        DataContext = new StoriesViewModel();
    }

    /// <summary>Moves keyboard focus into the story list so arrow keys read stories immediately.</summary>
    public void FocusStories()
    {
        if (StoriesList.Items.Count > 0)
        {
            StoriesList.SelectedIndex = Math.Max(0, StoriesList.SelectedIndex);
            StoriesList.ScrollIntoView(StoriesList.SelectedItem);
        }
        StoriesList.Focus();
        Keyboard.Focus(StoriesList);
    }

    private void UserControl_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ViewModel.OpenSelected();
            e.Handled = true;
            return;
        }

        // Story keys are remappable too, so the screen-reader preset frees up plain J, K and P.
        var shortcuts = ShortcutService.Instance;

        if (shortcuts.Matches(e.Key, Keyboard.Modifiers, "storyPause"))
        {
            _ = ViewModel.TogglePauseAsync();
            e.Handled = true;
            return;
        }

        if (shortcuts.Matches(e.Key, Keyboard.Modifiers, "storyNext"))
        {
            _ = ViewModel.NextAsync();
            e.Handled = true;
            return;
        }

        if (shortcuts.Matches(e.Key, Keyboard.Modifiers, "storyPrev"))
        {
            _ = ViewModel.PreviousAsync();
            e.Handled = true;
            return;
        }

        if (shortcuts.Matches(e.Key, Keyboard.Modifiers, "refresh"))
        {
            _ = ViewModel.RefreshAsync();
            e.Handled = true;
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e) => ViewModel.OpenSelected();

    private void Pause_Click(object sender, RoutedEventArgs e) => _ = ViewModel.TogglePauseAsync();

    private void Prev_Click(object sender, RoutedEventArgs e) => _ = ViewModel.PreviousAsync();

    private void Next_Click(object sender, RoutedEventArgs e) => _ = ViewModel.NextAsync();

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = ViewModel.RefreshAsync();
}
