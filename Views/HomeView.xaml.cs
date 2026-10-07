using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinInstagram.Models;
using WinInstagram.Services;
using WinInstagram.ViewModels;

namespace WinInstagram.Views;

/// <summary>
/// Native, screen-reader friendly view of the timeline. Posts are real accessible list items
/// driven by parsed timeline data, so reading the feed never depends on the web DOM.
/// </summary>
public partial class HomeView : UserControl
{
    public HomeViewModel ViewModel => (HomeViewModel)DataContext;

    /// <summary>Raised when the user asks for a fresh timeline; the shell reloads the engine.</summary>
    public event Action? RefreshRequested;

    /// <summary>Raised when the user shares the selected post; the shell copies the rich text.</summary>
    public event Action? ShareRequested;

    /// <summary>Raised when the user asks for a spoken description of the selected post.</summary>
    public event Action? DescribeRequested;

    public HomeView()
    {
        InitializeComponent();
        DataContext = new HomeViewModel();
    }

    /// <summary>Moves keyboard focus into the post list so arrow keys read posts immediately.</summary>
    public void FocusPosts()
    {
        if (PostsList.Items.Count > 0)
        {
            PostsList.SelectedIndex = Math.Max(0, PostsList.SelectedIndex);
            PostsList.ScrollIntoView(PostsList.SelectedItem);
        }
        PostsList.Focus();
        Keyboard.Focus(PostsList);
    }

    /// <summary>
    /// Brings the selected post into view without taking focus, so restoring a reading position
    /// does not move the keyboard away from whatever the user was doing.
    /// </summary>
    public void ScrollToSelected()
    {
        if (PostsList.SelectedItem != null)
        {
            PostsList.ScrollIntoView(PostsList.SelectedItem);
        }
    }

    private void UserControl_KeyDown(object sender, KeyEventArgs e)
    {
        // Every letter key is read from the user's own map, so a screen reader user who switched to
        // the modified preset keeps their plain letters to themselves here too.
        var shortcuts = ShortcutService.Instance;

        if (shortcuts.Matches(e.Key, Keyboard.Modifiers, "like"))
        {
            _ = ViewModel.ToggleLikeCurrentAsync();
            e.Handled = true;
            return;
        }

        if (shortcuts.Matches(e.Key, Keyboard.Modifiers, "share"))
        {
            ShareRequested?.Invoke();
            e.Handled = true;
            return;
        }

        if (shortcuts.Matches(e.Key, Keyboard.Modifiers, "refresh"))
        {
            RefreshRequested?.Invoke();
            e.Handled = true;
            return;
        }

        if (shortcuts.Matches(e.Key, Keyboard.Modifiers, "describe"))
        {
            DescribeRequested?.Invoke();
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                ViewModel.OpenSelectedInEngine();
                e.Handled = true;
                break;

            // Left and Right belong to the carousel, since Up and Down already read the list.
            case Key.Left:
                _ = ViewModel.StepCarouselAsync(-1);
                e.Handled = true;
                break;

            case Key.Right:
                _ = ViewModel.StepCarouselAsync(1);
                e.Handled = true;
                break;
        }
    }

    private void LikePost_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: FeedPost post })
        {
            ViewModel.SelectedPost = post;
            PostsList.SelectedItem = post;
        }
        _ = ViewModel.ToggleLikeCurrentAsync();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke();
}
