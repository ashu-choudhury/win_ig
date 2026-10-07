using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinInstagram.ViewModels;

namespace WinInstagram.Views;

/// <summary>
/// Native, screen-reader friendly view of a single Instagram profile. Everything the web page
/// shows visually (name, biography, counters, recent media) is presented as text and as real
/// accessible list items, so no part of it depends on the web DOM.
/// </summary>
public partial class ProfileView : UserControl
{
    public ProfileViewModel ViewModel => (ProfileViewModel)DataContext;

    public ProfileView()
    {
        InitializeComponent();
        DataContext = new ProfileViewModel();
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
        if (e.Key == Key.Enter)
        {
            ViewModel.OpenSelectedPost();
            e.Handled = true;
        }
    }
}
