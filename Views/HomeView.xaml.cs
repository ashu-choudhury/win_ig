using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinInstagram.Models;
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

    private void UserControl_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.L:
                _ = ViewModel.ToggleLikeCurrentAsync();
                e.Handled = true;
                break;

            case Key.Enter:
                ViewModel.OpenSelectedInEngine();
                e.Handled = true;
                break;

            case Key.R:
                RefreshRequested?.Invoke();
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
