using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinInstagram.Services;
using WinInstagram.ViewModels;

namespace WinInstagram.Views;

/// <summary>
/// Native, screen-reader friendly view of your saved posts and collections. Both lists are real
/// accessible list items built from Instagram's own saved payloads, so reading them never depends
/// on the web DOM. Opening a post is raised as an event so the shell stays the single owner of
/// engine navigation.
/// </summary>
public partial class SavedView : UserControl
{
    public SavedViewModel ViewModel => (SavedViewModel)DataContext;

    public SavedView()
    {
        InitializeComponent();
        DataContext = new SavedViewModel();

        ViewModel.OpenPostRequested += (url) => OpenPostRequested?.Invoke(url);
    }

    /// <summary>Raised with a permalink when the user asks to open a saved post.</summary>
    public event Action<string>? OpenPostRequested;

    /// <summary>Moves keyboard focus into the collection list so arrow keys read entries immediately.</summary>
    public void FocusCollections()
    {
        if (CollectionsList.Items.Count > 0)
        {
            CollectionsList.SelectedIndex = Math.Max(0, CollectionsList.SelectedIndex);
            CollectionsList.ScrollIntoView(CollectionsList.SelectedItem);
        }
        CollectionsList.Focus();
        Keyboard.Focus(CollectionsList);
    }

    /// <summary>Moves keyboard focus into the saved posts list.</summary>
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
        // Refresh is read from the user's map so it can be rebound and preset-aware.
        if (ShortcutService.Instance.Matches(e.Key, Keyboard.Modifiers, "refresh"))
        {
            _ = ViewModel.RefreshAsync();
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                // Enter on a collection just moves the reader into the posts it names.
                if (CollectionsList.IsKeyboardFocusWithin)
                {
                    FocusPosts();
                }
                else
                {
                    ViewModel.OpenSelectedPost();
                }
                e.Handled = true;
                break;
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = ViewModel.RefreshAsync();
}
