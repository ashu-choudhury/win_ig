using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinInstagram.ViewModels;

namespace WinInstagram.Views;

/// <summary>
/// Native, screen-reader friendly search panel. Results are real accessible list items built
/// from Instagram's own search response, so finding an account or a post never requires reading
/// the web page. Opening a result is raised as an event so the shell stays the only owner of
/// engine navigation.
/// </summary>
public partial class SearchView : UserControl
{
    public SearchViewModel ViewModel => (SearchViewModel)DataContext;

    public SearchView()
    {
        InitializeComponent();
        DataContext = new SearchViewModel();
    }

    /// <summary>Puts the caret in the query box so the user can type immediately.</summary>
    public void FocusSearch()
    {
        TxtQuery.Focus();
        Keyboard.Focus(TxtQuery);
        TxtQuery.SelectAll();
    }

    /// <summary>Puts focus on the result list so arrow keys read results.</summary>
    public void FocusResults()
    {
        ResultsList.Focus();
        Keyboard.Focus(ResultsList);
    }

    private void UserControl_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                // Enter means "search" while typing and "open" while reading the results.
                if (TxtQuery.IsKeyboardFocusWithin)
                {
                    _ = ViewModel.SearchAsync();
                }
                else
                {
                    ViewModel.OpenSelected();
                }
                e.Handled = true;
                break;

            case Key.Escape:
                if (TxtQuery.IsKeyboardFocusWithin)
                {
                    FocusResults();
                    e.Handled = true;
                }
                break;
        }
    }

    private void Search_Click(object sender, RoutedEventArgs e) => _ = ViewModel.SearchAsync();
}
